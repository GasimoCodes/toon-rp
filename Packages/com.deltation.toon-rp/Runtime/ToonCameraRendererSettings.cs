﻿using System;
using UnityEngine;

namespace DELTation.ToonRP
{
    [Serializable]
    public struct ToonCameraRendererSettings
    {
        public enum DepthPrePassMode
        {
            Off,
            Depth,
            DepthNormals,
        }

        public enum MsaaMode
        {
            Off = 1,
            _2x = 2,
            _4x = 4,
            _8x = 8,
        }

        public bool AllowHdr;
        public bool Stencil;
        public MsaaMode Msaa;
        [Range(0.5f, 2.0f)]
        public float RenderScale;
        public FilterMode RenderTextureFilterMode;
        public DepthPrePassMode DepthPrePass;

        public bool UseSrpBatching;
        public bool UseDynamicBatching;
    }
}