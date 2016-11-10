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
using CoiniumServ.Pools;
using CoiniumServ.Shares;
using CoiniumServ.Utils.Buffers;
using CoiniumServ.Utils.Extensions;
using CoiniumServ.Utils.Helpers;
using CoiniumServ.Utils.Numerics;
using Serilog;

namespace CoiniumServ.Vardiff
{
    public class VardiffManager:IVardiffManager
    {
        public IVardiffConfig Config { get; private set; }

        private readonly int _bufferSize;
        private readonly float _tMin;
        private readonly float _tMax;
        private readonly ILogger _logger;

        public VardiffManager(IPoolConfig poolConfig, IShareManager shareManager)
        {
            _logger = Log.ForContext<VardiffManager>().ForContext("Component", poolConfig.Coin.Name);

            Config = poolConfig.Stratum.Vardiff;

            if (!Config.Enabled)
                return;

            shareManager.ShareSubmitted += OnShare;

            var variance = Config.TargetTime * ((float)Config.VariancePercent / 100);
            _bufferSize = Config.RetargetTime / Config.TargetTime * 4;
            _tMin = Config.TargetTime - variance;
            _tMax = Config.TargetTime + variance;
        }

        private void OnShare(object sender, EventArgs e)
        {
            var shareArgs = (ShareEventArgs) e;
            var miner = shareArgs.Miner;

            if (miner == null)
                return;

            var now = TimeHelpers.NowInUnixTimestamp();

            if (miner.VardiffBuffer == null)
            {
                miner.LastVardiffRetarget = now - Config.RetargetTime / 2;
                miner.LastVardiffTimestamp = now;
                miner.VardiffBuffer = new RingBuffer(_bufferSize);
                return;
            }

            var sinceLast = now - miner.LastVardiffTimestamp; // how many seconds elapsed since last share?
            miner.VardiffBuffer.Append(sinceLast); // append it to vardiff buffer.
            miner.LastVardiffTimestamp = now;

            if (now - miner.LastVardiffRetarget < Config.RetargetTime && miner.VardiffBuffer.Size > 0) // check if we need a re-target.
                return;

            miner.LastVardiffRetarget = now;
            var average = miner.VardiffBuffer.Average;
            var deltaDiff = Config.TargetTime/average;

            if (average > _tMax && miner.Difficulty > Config.MinimumDifficulty)
            {
                if (deltaDiff*miner.Difficulty < Config.MinimumDifficulty)
                    deltaDiff = Config.MinimumDifficulty/miner.Difficulty;
            }
            else if (average < _tMin)
            {
                if (deltaDiff*miner.Difficulty > Config.MaximumDifficulty)
                    deltaDiff = Config.MaximumDifficulty/miner.Difficulty;
            }
            else
                return;

            var newDifficulty = miner.Difficulty*deltaDiff; // calculate the new difficulty.
            miner.SetDifficulty(newDifficulty); // set the new difficulty and send it.
            _logger.Debug("Difficulty updated to {0} for miner: {1:l}", miner.Difficulty, miner.Username);
            
            var bits = AlgorithmManager.Diff1 *  new BigRational( newDifficulty);
            
            //miner.SetTarget(bits.GetWholePart().ToByteArray().ToHexString());

            miner.VardiffBuffer.Clear();            
        }
    }
}
