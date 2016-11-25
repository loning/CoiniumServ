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

using System.Collections.Generic;
using CoiniumServ.Accounts;
using CoiniumServ.Mining;
using CoiniumServ.Payments;
using CoiniumServ.Persistance.Blocks;
using CoiniumServ.Persistance.Query;
using CoiniumServ.Server.Mining.Stratum;
using CoiniumServ.Shares;

namespace CoiniumServ.Persistance.Layers
{
    public interface IStorageLayer
    {

        double GetConfirmedBlocksRate(int num);
        /// <summary>
        /// Is the storage layer enabled?
        /// </summary>
        bool IsEnabled { get; }

        #region share storage

        /// <summary>
        /// Adds a new share.
        /// </summary>
        /// <param name="share"></param>
        void AddShare(IShare share);

        /// <summary>
        /// Move shares in current round to a new key with the block height.
        /// </summary>
        /// <param name="height">The new key to move the shares.</param>
        void MoveCurrentShares(int height);

        /// <summary>
        /// Move shares of an orphaned block to current key.
        /// </summary>
        /// <param name="block"></param>
        void MoveOrphanedShares(IPersistedBlock block);

        /// <summary>
        /// Remove shares associated with a round.
        /// </summary>
        /// <param name="round"></param>
        void RemoveShares(IPaymentRound round);

        /// <summary>
        /// Return shares within current key.
        /// </summary>
        /// <returns></returns>
        Dictionary<string, double> GetCurrentShares();

        /// <summary>
        /// Get shares for a given block.
        /// </summary>
        /// <param name="block"></param>
        /// <returns></returns>
        Dictionary<string, double> GetShares(IPersistedBlock block);

        #endregion

        #region block storage

        /// <summary>
        /// Adds a new block contained within the given share.
        /// </summary>
        /// <param name="share"></param>
        void AddBlock(IShare share);

        /// <summary>
        /// Updated a given block.
        /// </summary>
        /// <param name="block"></param>
        void UpdateBlock(IPersistedBlock block);

        /// <summary>
        /// Returns the block with given height.
        /// </summary>
        /// <param name="height"></param>
        /// <returns></returns>
        IPersistedBlock GetBlock(uint height);

        /// <summary>
        /// Returns blocks.
        /// </summary>
        /// <param name="paginationQuery"></param>
        /// <returns></returns>
        IList<IPersistedBlock> GetBlocks(IPaginationQuery paginationQuery);

        /// <summary>
        /// Returns blocks.
        /// </summary>
        /// <param name="paginationQuery"></param>
        /// <returns></returns>
        IList<IPersistedBlock> GetPaidBlocks(IPaginationQuery paginationQuery);

        /// <summary>
        /// Returns all unpaid blocks.
        /// </summary>
        /// <returns></returns>
        IEnumerable<IPersistedBlock> GetUnpaidBlocks();

        /// <summary>
        /// Returns all pending blocks.
        /// </summary>
        /// <returns></returns>
        IEnumerable<IPersistedBlock> GetPendingBlocks();

        /// <summary>
        /// Returns total blocks.
        /// </summary>
        /// <returns></returns>
        IDictionary<string, int> GetTotalBlocks();

        #endregion

        #region payments storage

        /// <summary>
        /// Adds a new payment.
        /// </summary>
        /// <param name="payment"></param>
        void AddPayment(IPayment payment);

        /// <summary>
        /// Updates a payment.
        /// </summary>
        /// <param name="payment"></param>
        void UpdatePayment(IPayment payment);

        /// <summary>
        /// Gets pending payments.
        /// </summary>
        /// <returns></returns>
        IList<IPayment> GetPendingPayments();

        /// <summary>
        /// Returns payments for block.
        /// </summary>
        /// <param name="height"></param>
        /// <returns></returns>
        IList<IPaymentDetails> GetPaymentsForBlock(uint height);

        /// <summary>
        /// Returns payments for a specific account.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="paginationQuery"></param>
        /// <returns></returns>
        IList<IPaymentDetails> GetPaymentsForAccount(int id, IPaginationQuery paginationQuery);

        IPaymentDetails GetPaymentDetailsByTransactionId(uint id);

        IPaymentDetails GetPaymentDetailsByPaymentId(uint id);

        /// <summary>
        /// Adds a transaction.
        /// </summary>
        /// <param name="transaction"></param>
        void AddTransaction(ITransaction transaction);

        #endregion

        #region account storage

        /// <summary>
        /// Adds a new account.
        /// </summary>
        /// <param name="account"></param>
        void AddAccount(IAccount account);

        /// <summary>
        /// Returns the account with given username.
        /// </summary>
        /// <param name="username"></param>
        /// <returns></returns>
        IAccount GetAccountByUsername(string username);

        /// <summary>
        /// Returns the account with given address.
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        IAccount GetAccountByAddress(string address);

        /// <summary>
        /// Returns the account with given Id.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        IAccount GetAccountById(int id);

        #endregion

        #region worker storage

        /// <summary>
        /// Authenticates a miner.
        /// </summary>
        /// <param name="miner"></param>
        /// <returns></returns>
        bool Authenticate(IMiner miner);

        /// <summary>
        /// Updated difficulty for a stratum miner.
        /// </summary>
        /// <param name="miner"></param>
        void UpdateDifficulty(IStratumMiner miner);

        #endregion

        #region statistics storage

        IDictionary<string, double> GetHashrateData(int since);

        void DeleteExpiredHashrateData(int until);

        #endregion
    }
}
