﻿#region License
// 
//     CoiniumServ - Crypto Currency Mining Pool Server Software
//     Copyright (C) 2013 - 2014, CoiniumServ Project - http://www.coinium.org
//     http://www.coiniumserv.com - https://github.com/CoiniumServ/CoiniumServ
// 
//     This software is dual-licensed: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//    
//     For the terms of this license, see licenses/gpl_v3.txt.
// 
//     Alternatively, you can license this software under a commercial
//     license or white-label it as set out in licenses/commercial.txt.
// 
#endregion

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using AustinHarris.JsonRpc;
using CoiniumServ.Daemon;
using CoiniumServ.Daemon.Exceptions;
using CoiniumServ.Jobs.Tracker;
using CoiniumServ.Persistance.Layers;
using CoiniumServ.Pools;
using CoiniumServ.Server.Mining.Getwork;
using CoiniumServ.Server.Mining.Stratum;
using CoiniumServ.Server.Mining.Stratum.Errors;
using CoiniumServ.Utils.Extensions;
using Serilog;

namespace CoiniumServ.Shares
{
    public class ShareManager : IShareManager
    {
        public event EventHandler BlockFound;

        public event EventHandler ShareSubmitted;

        private readonly IJobTracker _jobTracker;

        private readonly IDaemonClient _daemonClient;

        private readonly IStorageLayer _storageLayer;

        private readonly IPoolConfig _poolConfig;

        private string _poolAccount;

        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShareManager" /> class.
        /// </summary>
        /// <param name="poolConfig"></param>
        /// <param name="daemonClient"></param>
        /// <param name="jobTracker"></param>
        /// <param name="storageLayer"></param>
        public ShareManager(IPoolConfig poolConfig, IDaemonClient daemonClient, IJobTracker jobTracker, IStorageLayer storageLayer)
        {
            _poolConfig = poolConfig;
            _daemonClient = daemonClient;
            _jobTracker = jobTracker;
            _storageLayer = storageLayer;
            _logger = Log.ForContext<ShareManager>().ForContext("Component", poolConfig.Coin.Name);

            FindPoolAccount();
        }


        static Semaphore sem = new Semaphore(5, 5);
        /// <summary>
        /// Processes the share.
        /// </summary>
        /// <param name="miner">The miner.</param>
        /// <param name="jobId">The job identifier.</param>
        /// <param name="extraNonce2">The extra nonce2.</param>
        /// <param name="nTimeString">The n time string.</param>
        /// <param name="nonceString">The nonce string.</param>
        /// <returns></returns>
        public IShare ProcessShare(IStratumMiner miner, string jobId, string nTime, string extraNonce2, string equihashSolution)
        {
            // check if the job exists
            var id = Convert.ToUInt64(jobId, 16);
            var job = _jobTracker.Get(id);

            // create the share
            var share = new Share(miner, id, job, extraNonce2, nTime, equihashSolution);



            if (share.IsValid)
            {
                if (miner.ValidShareCount % 1000 == 0)
                {
                    share.FillBlockHex();
                    if (sem.WaitOne())
                    {
                        try
                        {
                            var ret = _daemonClient.SubmitBlock(share.BlockHex.ToHexString()); // submit the block.
                            if (ret == "invalid-solution")
                            {
                                //change this will ban the miner
                                miner.InvalidShareCount = int.MaxValue - 1000;
                                _logger.Debug("Share invalid at {0:0.00}/{1} by miner {2:l}", share.Difficulty,
                                    miner.Difficulty, miner.Username);
                                
                            }
                        }
                        finally
                        {
                            sem.Release();
                        }
                    }
                }
                HandleValidShare(share);

            }
            else
                HandleInvalidShare(share);

            OnShareSubmitted(new ShareEventArgs(miner));  // notify the listeners about the share.

            return share;
        }

        public IShare ProcessShare(IGetworkMiner miner, string data)
        {
            throw new NotImplementedException();
        }

        private void HandleValidShare(IShare share)
        {
            var miner = (IStratumMiner)share.Miner;


            miner.ValidShareCount++;


            _storageLayer.AddShare(share); // commit the share.
            _logger.Debug("Share accepted at {0:0.00}/{1} by miner {2:l}", share.Difficulty, miner.Difficulty, miner.Username);

            // check if share is a block candidate
            if (!share.IsBlockCandidate)
                return;

            // submit block candidate to daemon.
            var accepted = SubmitBlock(share);

            if (!accepted) // if block wasn't accepted
                return; // just return as we don't need to notify about it and store it.

            OnBlockFound(EventArgs.Empty); // notify the listeners about the new block.

            _storageLayer.AddBlock(share); // commit the block details to storage.
            _storageLayer.MoveCurrentShares(share.Height); // move associated shares to new key.
        }

        private void HandleInvalidShare(IShare share)
        {
            var miner = (IStratumMiner)share.Miner;
            miner.InvalidShareCount++;

            JsonRpcException exception = null; // the exception determined by the stratum error code.
            switch (share.Error)
            {
                case ShareError.DuplicateShare:
                    exception = new DuplicateShareError(share.Nonce);
                    break;
                case ShareError.IncorrectExtraNonce2Size:
                    exception = new OtherError("Incorrect extranonce2 size");
                    break;
                case ShareError.IncorrectNTimeSize:
                    exception = new OtherError("Incorrect nTime size");
                    break;
                case ShareError.IncorrectNonceSize:
                    exception = new OtherError("Incorrect nonce size");
                    break;
                case ShareError.JobNotFound:
                    exception = new JobNotFoundError(share.JobId);
                    break;
                case ShareError.LowDifficultyShare:
                    exception = new LowDifficultyShare(share.Difficulty);
                    break;
                case ShareError.NTimeOutOfRange:
                    exception = new OtherError("nTime out of range");
                    break;
            }
            JsonRpcContext.SetException(exception); // set the stratum exception within the json-rpc reply.

            Debug.Assert(exception != null); // exception should be never null when the share is marked as invalid.
            _logger.Debug("Rejected share by miner {0:l}, reason: {1:l}", miner.Username, exception.message);
        }

        private bool SubmitBlock(IShare share)
        {
            // TODO: we should try different submission techniques and probably more then once: https://github.com/ahmedbodi/stratum-mining/blob/master/lib/bitcoin_rpc.py#L65-123

            try
            {
                if (_poolConfig.Coin.Options.SubmitBlockSupported) // see if submitblock() is available.
                    _daemonClient.SubmitBlock(share.BlockHex.ToHexString()); // submit the block.
                else
                    _daemonClient.GetBlockTemplate(share.BlockHex.ToHexString()); // use getblocktemplate() if submitblock() is not supported.

                var block = _daemonClient.GetBlock(share.BlockHash.ToHexString()); // query the block.

                if (block == null) // make sure the block exists
                    return false;

                if (block.Confirmations == -1) // make sure the block is accepted.
                {
                    _logger.Debug("Submitted block [{0}] is orphaned; [{1:l}]", block.Height, block.Hash);
                    return false;
                }

                var expectedTxHash = share.CoinbaseHash.Bytes.ReverseBuffer().ToHexString(); // calculate our expected generation transactions's hash
                var genTxHash = block.Tx.First(); // read the hash of very first (generation transaction) of the block

                if (expectedTxHash != genTxHash) // make sure our calculated generated transaction and one reported by coin daemon matches.
                {
                    _logger.Debug("Submitted block [{0}] doesn't seem to belong us as reported generation transaction hash [{1:l}] doesn't match our expected one [{2:l}]", block.Height, genTxHash, expectedTxHash);
                    return false;
                }

                var genTx = _daemonClient.GetTransaction(block.Tx.First()); // get the generation transaction.

                // make sure we were able to read the generation transaction
                if (genTx == null)
                {
                    _logger.Debug("Submitted block [{0}] doesn't seem to belong us as we can't read the generation transaction on our records [{1:l}]", block.Height, block.Tx.First());
                    return false;
                }

                var poolOutput = genTx.GetPoolOutput(_poolConfig.Wallet.Adress, _poolAccount); // get the output that targets pool's central address.

                // make sure the blocks generation transaction contains our central pool wallet address
                if (poolOutput == null)
                {
                    _logger.Debug("Submitted block [{0}] doesn't seem to belong us as generation transaction doesn't contain an output for pool's central wallet address: {0:}", block.Height, _poolConfig.Wallet.Adress);
                    return false;
                }

                // if the code flows here, then it means the block was succesfully submitted and belongs to us.
                share.SetFoundBlock(block, genTx); // assign the block to share.

                _logger.Information("Found block [{0}] with hash [{1:l}]", share.Height, share.BlockHash.ToHexString());

                return true;
            }
            catch (RpcException e)
            {
                // unlike BlockProcessor's detailed exception handling and decision making based on the error,
                // here in share-manager we only one-shot submissions. If we get an error, basically we just don't care about the rest
                // and flag the submission as failed.
                _logger.Debug("We thought a block was found but it was rejected by the coin daemon; [{0:l}] - reason; {1:l}", share.BlockHash.ToHexString(), e.Message);
                return false;
            }
        }

        private void OnBlockFound(EventArgs e)
        {
            var handler = BlockFound;

            if (handler != null)
                handler(this, e);
        }

        private void OnShareSubmitted(EventArgs e)
        {
            var handler = ShareSubmitted;

            if (handler != null)
                handler(this, e);
        }

        private void FindPoolAccount()
        {
            try
            {
                _poolAccount = !_poolConfig.Coin.Options.UseDefaultAccount // if UseDefaultAccount is not set
                    ? _daemonClient.GetAccount(_poolConfig.Wallet.Adress) // find the account of the our pool address.
                    : ""; // use the default account.
            }
            catch (RpcException e)
            {
                _logger.Error("Error getting account for pool central wallet address: {0:l} - {1:l}", _poolConfig.Wallet.Adress, e.Message);
            }
        }
    }
}
