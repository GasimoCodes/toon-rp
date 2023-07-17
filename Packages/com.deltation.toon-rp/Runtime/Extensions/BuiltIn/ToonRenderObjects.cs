﻿using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DELTation.ToonRP.Extensions.BuiltIn
{
    public class ToonRenderObjects : ToonRenderingExtensionBase
    {
        private readonly List<ShaderTagId> _lightModeTags = new();
        private Camera _camera;
        private ToonCameraRendererSettings _cameraRendererSettings;
        private ScriptableRenderContext _context;
        private CullingResults _cullingResults;
        private ToonRenderObjectsSettings _settings;

        public override void Setup(in ToonRenderingExtensionContext context,
            IToonRenderingExtensionSettingsStorage settingsStorage)
        {
            base.Setup(in context, settingsStorage);
            _settings = settingsStorage.GetSettings<ToonRenderObjectsSettings>(this);
            _context = context.ScriptableRenderContext;
            _camera = context.Camera;
            _cameraRendererSettings = context.CameraRendererSettings;
            _cullingResults = context.CullingResults;
        }

        public override void Render()
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            string passName = !string.IsNullOrWhiteSpace(_settings.PassName)
                ? _settings.PassName
                : ToonRpPassId.RenderObjects;

            using (new ProfilingScope(cmd, NamedProfilingSampler.Get(passName)))
            {
                bool overrideLightModeTags = false;
                if (_settings.Filters.LightModeTags is { Length: > 0 })
                {
                    overrideLightModeTags = true;
                    _lightModeTags.Clear();
                    foreach (string lightMode in _settings.Filters.LightModeTags)
                    {
                        _lightModeTags.Add(new ShaderTagId(lightMode));
                    }
                }

                var cameraOverride = new ToonCameraOverride(_camera);
                cameraOverride.OverrideIfEnabled(cmd, _settings.Overrides.Camera);
                _context.ExecuteCommandBufferAndClear(cmd);

                bool opaque = _settings.Filters.Queue == ToonRenderObjectsSettings.FilterSettings.RenderQueue.Opaque;
                var sortingSettings = new SortingSettings(_camera)
                {
                    criteria = opaque
                        ? SortingCriteria.CommonOpaque
                        : SortingCriteria.CommonTransparent,
                };
                RenderQueueRange renderQueueRange = opaque ? RenderQueueRange.opaque : RenderQueueRange.transparent;
                ToonCameraRenderer.DrawGeometry(_cameraRendererSettings, ref _context, _cullingResults, sortingSettings,
                    renderQueueRange, _settings.Filters.LayerMask, null,
                    overrideLightModeTags ? _lightModeTags : null,
                    false
                );

                cameraOverride.RestoreIfEnabled(cmd);
            }

            _context.ExecuteCommandBufferAndClear(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}