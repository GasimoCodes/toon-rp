﻿using System;
using UnityEngine;

namespace DELTation.ToonRP.PostProcessing.BuiltIn
{
    [CreateAssetMenu(menuName = Path + "Sharpen Test")]
    public class ToonSharpenAssetTest : ToonPostProcessingPassAsset<ToonSharpenSettings>
    {
        private void Reset()
        {
            Settings = new ToonSharpenSettings
            {
                Amount = 0.8f,
            };
        }

        public override int Order() => Settings.Order switch
        {
            ToonSharpenSettings.PassOrder.PreUpscale => ToonPostProcessingPassOrders.SharpenPreUpscale,
            ToonSharpenSettings.PassOrder.PostUpscale => ToonPostProcessingPassOrders.SharpenPostUpscale,
            _ => throw new ArgumentOutOfRangeException(),
        };

        public override IToonPostProcessingPass CreatePass() => new ToonSharpenTest();

        protected override string[] ForceIncludedShaderNames() => new[] { ToonSharpenTest.ShaderName };
    }
}