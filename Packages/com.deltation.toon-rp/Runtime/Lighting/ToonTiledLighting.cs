﻿using System;
using System.Runtime.InteropServices;
using DELTation.ToonRP.Extensions;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Rendering;

namespace DELTation.ToonRP.Lighting
{
    public class ToonTiledLighting : IDisposable
    {
        private const int TileSize = 16;
        public const int MinLightsPerTile = 8;
        public const int MaxLightsPerTile = 64;
        private const int FrustumSize = 4 * 4 * sizeof(float);
        private const int LightIndexListBaseIndexOffset = 2;

        public const string SetupComputeShaderName = "TiledLighting_Setup";
        public const string ComputeFrustumsComputeShaderName = "TiledLighting_ComputeFrustums";
        public const string CullLightsComputeShaderName = "TiledLighting_CullLights";
        public const string TiledLightingKeywordName = "_TOON_RP_TILED_LIGHTING";
        private static readonly int ReservedLightsPerTileId = Shader.PropertyToID("_ReservedLightsPerTile");


        private readonly ComputeShaderKernel _computeFrustumsKernel;
        private readonly ComputeShaderKernel _cullLightsKernel;
        private readonly ToonStructuredComputeBuffer _frustumsBuffer = new(FrustumSize);
        private readonly ToonStructuredComputeBuffer _lightGrid = new(sizeof(uint) * 2);
        private readonly ToonStructuredComputeBuffer _lightIndexList = new(sizeof(uint));
        private readonly ToonLighting _lighting;
        private readonly ComputeShaderKernel _setupKernel;
        private readonly GlobalKeyword _tiledLightingKeyword;
        private readonly ToonStructuredComputeBuffer _tiledLightsBuffer =
            new(Marshal.SizeOf<TiledLight>(), ToonLighting.MaxAdditionalLightCountTiled / 8);

        private ScriptableRenderContext _context;
        private bool _enabled;
        private int _reservedLightsPerTile;

        private float _screenHeight;
        private float _screenWidth;
        private uint _tilesX;
        private uint _tilesY;

        public ToonTiledLighting(ToonLighting lighting)
        {
            _lighting = lighting;
            _tiledLightingKeyword = GlobalKeyword.Create(TiledLightingKeywordName);

            ComputeShader clearCountersComputeShader = Resources.Load<ComputeShader>(SetupComputeShaderName);
            _setupKernel = new ComputeShaderKernel(clearCountersComputeShader, 0);

            ComputeShader computeFrustumsComputeShader =
                Resources.Load<ComputeShader>(ComputeFrustumsComputeShaderName);
            _computeFrustumsKernel = new ComputeShaderKernel(computeFrustumsComputeShader, 0);

            ComputeShader cullLightsComputeShader = Resources.Load<ComputeShader>(CullLightsComputeShaderName);
            _cullLightsKernel = new ComputeShaderKernel(cullLightsComputeShader, 0);
        }

        private int TotalTilesCount => (int) (_tilesX * _tilesY);

        public void Dispose()
        {
            _frustumsBuffer?.Dispose();
            _lightGrid?.Dispose();
            _lightIndexList?.Dispose();
            _tiledLightsBuffer?.Dispose();
        }

        public void Setup(in ScriptableRenderContext context, in ToonRenderingExtensionContext toonContext)
        {
            _context = context;
            _enabled = toonContext.CameraRendererSettings.IsTiledLightingEnabledAndSupported;

            if (!_enabled)
            {
                return;
            }

            ToonCameraRenderTarget renderTarget = toonContext.CameraRenderTarget;
            _screenWidth = renderTarget.Width;
            _screenHeight = renderTarget.Height;
            _tilesX = (uint) Mathf.CeilToInt(_screenWidth / TileSize);
            _tilesY = (uint) Mathf.CeilToInt(_screenHeight / TileSize);
            int totalTilesCount = (int) (_tilesX * _tilesY);

            _frustumsBuffer.Update(totalTilesCount);
            _lightGrid.Update(totalTilesCount * 2);

            _reservedLightsPerTile = Mathf.Clamp(
                toonContext.CameraRendererSettings.MaxLightsPerTile,
                MinLightsPerTile,
                MaxLightsPerTile
            );
            _lightIndexList.Update(totalTilesCount * _reservedLightsPerTile * 2 + LightIndexListBaseIndexOffset);
            _cullLightsKernel.Cs.SetInt(ReservedLightsPerTileId, _reservedLightsPerTile);

            _lighting.GetTiledAdditionalLightsBuffer(out _, out int tiledLightsCount);
            _tiledLightsBuffer.Update(tiledLightsCount);

            _computeFrustumsKernel.Setup();
        }

        public void CullLights()
        {
            CommandBuffer cmd = CommandBufferPool.Get();

            cmd.SetKeyword(_tiledLightingKeyword, _enabled);

            if (_enabled)
            {
                using (new ProfilingScope(cmd, NamedProfilingSampler.Get(ToonRpPassId.TiledLighting)))
                {
                    _lighting.GetTiledAdditionalLightsBuffer(out TiledLight[] tiledLights, out int tiledLightsCount);
                    _tiledLightsBuffer.Buffer.SetData(tiledLights, 0, 0, tiledLightsCount);
                    cmd.SetGlobalBuffer(ShaderIds.LightsId, _tiledLightsBuffer.Buffer);

                    cmd.SetGlobalVector(ShaderIds.ScreenDimensionsId,
                        new Vector4(_screenWidth, _screenHeight)
                    );
                    cmd.SetGlobalInt(ShaderIds.TilesXId, (int) _tilesX);
                    cmd.SetGlobalInt(ShaderIds.TilesYId, (int) _tilesY);
                    cmd.SetGlobalInt(ShaderIds.CurrentLightIndexListOffsetId, 0);
                    cmd.SetGlobalInt(ShaderIds.CurrentLightGridOffsetId, 0);

                    using (new ProfilingScope(cmd, NamedProfilingSampler.Get("Clear Counters")))
                    {
                        cmd.SetGlobalBuffer(ShaderIds.LightIndexListId, _lightIndexList.Buffer);
                        _setupKernel.Dispatch(cmd, 1);
                    }

                    using (new ProfilingScope(cmd, NamedProfilingSampler.Get("Compute Frustums")))
                    {
                        cmd.SetGlobalBuffer(ShaderIds.FrustumsId, _frustumsBuffer.Buffer);
                        _computeFrustumsKernel.Dispatch(cmd, _tilesX, _tilesY);
                    }

                    using (new ProfilingScope(cmd, NamedProfilingSampler.Get("Cull Lights")))
                    {
                        // Frustum and light index list buffers are already bound
                        cmd.SetGlobalBuffer(ShaderIds.LightGridId, _lightGrid.Buffer);
                        _cullLightsKernel.Dispatch(cmd, (uint) _screenWidth, (uint) _screenHeight);
                    }
                }
            }

            _context.ExecuteCommandBufferAndClear(cmd);
            CommandBufferPool.Release(cmd);
        }

        public static void PrepareForOpaqueGeometry(CommandBuffer cmd)
        {
            PrepareForGeometryPass(cmd, 0);
        }

        public void PrepareForTransparentGeometry(CommandBuffer cmd)
        {
            PrepareForGeometryPass(cmd, TotalTilesCount);
        }

        private static void PrepareForGeometryPass(CommandBuffer cmd, int offset)
        {
            cmd.SetGlobalInt(ShaderIds.CurrentLightIndexListOffsetId,
                LightIndexListBaseIndexOffset + offset * ReservedLightsPerTileId
            );
            cmd.SetGlobalInt(ShaderIds.CurrentLightGridOffsetId, offset);
        }

        private static class ShaderIds
        {
            public static readonly int LightsId = Shader.PropertyToID("_TiledLighting_Lights");
            public static readonly int ScreenDimensionsId = Shader.PropertyToID("_TiledLighting_ScreenDimensions");
            public static readonly int LightIndexListId = Shader.PropertyToID("_TiledLighting_LightIndexList");
            public static readonly int FrustumsId = Shader.PropertyToID("_TiledLighting_Frustums");
            public static readonly int LightGridId = Shader.PropertyToID("_TiledLighting_LightGrid");
            public static readonly int TilesYId = Shader.PropertyToID("_TiledLighting_TilesY");
            public static readonly int TilesXId = Shader.PropertyToID("_TiledLighting_TilesX");
            public static readonly int CurrentLightIndexListOffsetId =
                Shader.PropertyToID("_TiledLighting_CurrentLightIndexListOffset");
            public static readonly int CurrentLightGridOffsetId =
                Shader.PropertyToID("_TiledLighting_CurrentLightGridOffset");
        }

        private class ComputeShaderKernel
        {
            private readonly int _kernelIndex;
            private uint _groupSizeX;
            private uint _groupSizeY;
            private uint _groupSizeZ;

            public ComputeShaderKernel([NotNull] ComputeShader computeShader, int kernelIndex)
            {
                Cs = computeShader ? computeShader : throw new ArgumentNullException(nameof(computeShader));
                _kernelIndex = kernelIndex;
                Setup();
            }

            public ComputeShader Cs { get; }

            public void Setup()
            {
                Cs.GetKernelThreadGroupSizes(_kernelIndex,
                    out _groupSizeX, out _groupSizeY, out _groupSizeZ
                );
            }

            public void Dispatch(CommandBuffer cmd, uint totalThreadsX, uint totalThreadsY = 1, uint totalThreadsZ = 1)
            {
                cmd.DispatchCompute(Cs, _kernelIndex,
                    Mathf.CeilToInt((float) totalThreadsX / _groupSizeX),
                    Mathf.CeilToInt((float) totalThreadsY / _groupSizeY),
                    Mathf.CeilToInt((float) totalThreadsZ / _groupSizeZ)
                );
            }
        }
    }
}