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
using CoiniumServ.Algorithms;
using CoiniumServ.Coin.Coinbase;
using CoiniumServ.Cryptology;
using CoiniumServ.Daemon.Responses;
using CoiniumServ.Jobs;
using CoiniumServ.Mining;
using CoiniumServ.Server.Mining.Stratum;
using CoiniumServ.Utils.Extensions;
using CoiniumServ.Utils.Helpers;
using CoiniumServ.Utils.Numerics;
using Gibbed.IO;

namespace CoiniumServ.Shares
{
    public class Share : IShare
    {
        public bool IsValid { get { return Error == ShareError.None; } }
        public bool IsBlockCandidate { get; private set; }
        public Block Block { get; private set; }
        public Transaction GenerationTransaction { get; private set; }
        public bool IsBlockAccepted { get { return Block != null; } }
        public IMiner Miner { get; private set; }
        public ShareError Error { get; private set; }
        public UInt64 JobId { get; private set; }
        public IJob Job { get; private set; }
        public int Height { get; private set; }
        public UInt32 NTime { get; private set; }
        public UInt32 Nonce { get; private set; }
        public UInt32 ExtraNonce1 { get; private set; }
        public string ExtraNonce2 { get; private set; }
        public byte[] CoinbaseBuffer { get; private set; }
        public Hash CoinbaseHash { get; private set; }
        public byte[] MerkleRoot { get; private set; }
        public byte[] HeaderBuffer { get; private set; }
        public byte[] HeaderHash { get; private set; }
        public BigInteger HeaderValue { get; private set; }
        public Double Difficulty { get; private set; }
        public double BlockDiffAdjusted { get; private set; }
        public byte[] BlockHex { get; private set; }
        public byte[] BlockHash { get; private set; }

        public Share(IStratumMiner miner, UInt64 jobId, IJob job, string extraNonce2, string nTimeString, string nSolution)
        {
            Miner = miner;
            JobId = jobId;
            Job = job;
            Error = ShareError.None;

            var submitTime = TimeHelpers.NowInUnixTimestamp(); // time we recieved the share from miner.

            if (Job == null)
            {
                Error = ShareError.JobNotFound;
                return;
            }

            // check size of miner supplied extraNonce2
            if (extraNonce2.Length / 2 != ExtraNonce.ExpectedExtraNonce2Size)
            {
                Error = ShareError.IncorrectExtraNonce2Size;
                return;
            }
            ExtraNonce2 = extraNonce2; // set extraNonce2 for the share.

            // check size of miner supplied nTime.
            if (nTimeString.Length != 8)
            {
                Error = ShareError.IncorrectNTimeSize;
                return;
            }
            NTime = Convert.ToUInt32(nTimeString.HexToByteArray().ReverseBuffer().ToHexString(), 16); // read ntime for the share

            // make sure NTime is within range.
            if (NTime < job.BlockTemplate.CurTime || NTime > submitTime + 7200)
            {
                Error = ShareError.NTimeOutOfRange;
                return;
            }
            
            // set job supplied parameters.
            Height = job.BlockTemplate.Height; // associated job's block height.
            ExtraNonce1 = miner.ExtraNonce; // extra nonce1 assigned to miner.

            // check for duplicate shares.
            if (!Job.RegisterShare(this)) // try to register share with the job and see if it's duplicated or not.
            {
                Error = ShareError.DuplicateShare;
                return;
            }

            // construct the coinbase.
            CoinbaseBuffer = Serializers.SerializeCoinbase(Job, ExtraNonce1);
            CoinbaseHash = Coin.Coinbase.Utils.HashCoinbase(CoinbaseBuffer);

            string nonceString = extraNonce2.HexToByteArray().ReverseBuffer().ToHexString() + ExtraNonce1.BigEndian().ToString("x8") ;

            byte[] nonce = nonceString.HexToByteArray();


            // create the merkle root.
            MerkleRoot = Job.MerkleTree.WithFirst(CoinbaseHash).ReverseBuffer();

            // create the block headers
            {
                HeaderBuffer = Serializers.SerializeHeader(Job, MerkleRoot, nonce, NTime,
                    nSolution.HexToByteArray().ReverseBuffer());
                HeaderHash = Job.HashAlgorithm.Hash(HeaderBuffer);

            }
            
            HeaderValue = new BigInteger(HeaderHash);

            // calculate the share difficulty
            Difficulty = ((double)new BigRational(AlgorithmManager.Diff1, HeaderValue)) * Job.HashAlgorithm.Multiplier;

            // calculate the block difficulty
            BlockDiffAdjusted = Job.Difficulty * Job.HashAlgorithm.Multiplier;

            // check if block candicate
            if (Job.Target >= HeaderValue)
            {
                IsBlockCandidate = true;
                BlockHex = Serializers.SerializeBlock(Job, HeaderBuffer, CoinbaseBuffer, miner.Pool.Config.Coin.Options.IsProofOfStakeHybrid);
                BlockHash = HeaderBuffer.DoubleDigest().ReverseBuffer();
            }
            else
            {
                IsBlockCandidate = false;
                BlockHash = HeaderBuffer.DoubleDigest().ReverseBuffer();

                // Check if share difficulty reaches miner difficulty.
                var lowDifficulty = Difficulty / miner.Difficulty < 0.99; // share difficulty should be equal or more then miner's target difficulty.

                if (!lowDifficulty) // if share difficulty is high enough to match miner's current difficulty.
                    return; // just accept the share.

                if (Difficulty >= miner.PreviousDifficulty) // if the difficulty matches miner's previous difficulty before the last vardiff triggered difficulty change
                    return; // still accept the share.

                // if the share difficulty can't match miner's current difficulty or previous difficulty                
                Error = ShareError.LowDifficultyShare; // then just reject the share with low difficult share error.
            }
        }

        public void SetFoundBlock(Block block, Transaction genTx)
        {
            Block = block;
            GenerationTransaction = genTx;
        }
    }
}
