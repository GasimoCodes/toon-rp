﻿using System;
using UnityEngine;

namespace ToonRP.Runtime.Shadows
{
    [Serializable]
    public struct ToonShadowSettings
    {
        public enum ShadowMode
        {
            Off = 0,
            Vsm,
            Blobs,
        }

        public ShadowMode Mode;

        [Range(0.0f, 1.0f)]
        public float Threshold;
        public bool CrispAntiAliased;
        [Range(0.0f, 1.0f)]
        public float Smoothness;
        [Min(0f)]
        public float MaxDistance;
        [Range(0.001f, 1f)]
        public float DistanceFade;

        public ToonVsmShadowSettings Vsm;
        public ToonBlobShadowsSettings Blobs;
    }
}