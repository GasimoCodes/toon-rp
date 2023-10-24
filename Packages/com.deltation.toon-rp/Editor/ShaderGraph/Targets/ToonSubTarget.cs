using System;
using DELTation.ToonRP.Editor.ShaderGUI;
using DELTation.ToonRP.Editor.ShaderGUI.ShaderGraph;
using UnityEditor;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;
using static DELTation.ToonRP.Editor.ToonShaderUtils;
using RenderQueue = UnityEngine.Rendering.RenderQueue;

namespace DELTation.ToonRP.Editor.ShaderGraph.Targets
{
    internal abstract class ToonSubTarget : SubTarget<ToonTarget>, IHasMetadata
    {
        private static readonly GUID SourceCodeGuid = new("224570bab10c13c4f8b7ca798294dee1"); // ToonSubTarget.cs

        protected abstract ShaderID ShaderID { get; }

        public virtual string identifier => GetType().Name;

        public virtual ScriptableObject GetMetadataObject(GraphDataReadOnly graphData)
        {
            ToonMetadata metadata = ScriptableObject.CreateInstance<ToonMetadata>();
            metadata.ShaderID = ShaderID;
            metadata.AllowMaterialOverride = target.AllowMaterialOverride;
            metadata.AlphaMode = target.AlphaMode;
            metadata.CastShadows = target.CastShadows;
            return metadata;
        }

        public override void GetFields(ref TargetFieldContext context) { }

        public override void Setup(ref TargetSetupContext context)
        {
            context.AddAssetDependency(SourceCodeGuid, AssetCollection.Flags.SourceDependency);
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            context.AddBlock(ToonBlockFields.SurfaceDescription.Alpha,
                target.SurfaceType == SurfaceType.Transparent || target.AlphaClip || target.AllowMaterialOverride
            );
            context.AddBlock(ToonBlockFields.SurfaceDescription.AlphaClipThreshold,
                target.AlphaClip || target.AllowMaterialOverride
            );
        }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange,
            Action<string> registerUndo)
        {
            target.AddDefaultMaterialOverrideGUI(ref context, onChange, registerUndo);
            target.AddDefaultSurfacePropertiesGUI(ref context, onChange, registerUndo, false);
        }

        public override void ProcessPreviewMaterial(Material material)
        {
            base.ProcessPreviewMaterial(material);

            material.SetFloat(PropertyNames.RenderQueue, (float) target.RenderQueue);
            material.SetFloat(PropertyNames.QueueControl, (float) QueueControl.Auto);
            material.SetFloat(PropertyNames.QueueOffset, 0.0f);

            if (target.AllowMaterialOverride)
            {
                // copy our target's default settings into the material
                // (technically not necessary since we are always recreating the material from the shader each time,
                // which will pull over the defaults from the shader definition)
                // but if that ever changes, this will ensure the defaults are set
                material.SetFloat(PropertyNames.SurfaceType, (float) target.SurfaceType);
                material.SetFloat(PropertyNames.BlendMode, (float) target.AlphaMode);
                material.SetFloat(PropertyNames.AlphaClipping, target.AlphaClip ? 1.0f : 0.0f);
                material.SetFloat(PropertyNames.ForceDisableFogPropertyName, !target.Fog ? 1.0f : 0.0f);
                material.SetFloat(PropertyNames.RenderFace, (int) target.RenderFace);
                material.SetFloat(PropertyNames.CastShadows, target.CastShadows ? 1.0f : 0.0f);
                material.SetFloat(PropertyNames.ZWriteControl, (float) target.ZWriteControl);
                material.SetFloat(PropertyNames.ZTest, (float) target.ZTestMode);
            }

            material.SetFloat(PropertyNames.ControlOutlinesStencilLayer,
                target.ControlOutlinesStencilLayerEffectivelyEnabled ? 1.0f : 0.0f
            );

            if (target.ControlOutlinesStencilLayerEffectivelyEnabled || target.AllowMaterialOverride)
            {
                material.SetFloat(PropertyNames.OutlinesStencilLayer, 0.0f);
                material.SetFloat(PropertyNames.ForwardStencilRef, 0.0f);
                material.SetFloat(PropertyNames.ForwardStencilWriteMask, 0.0f);
                material.SetFloat(PropertyNames.ForwardStencilComp, 0.0f);
                material.SetFloat(PropertyNames.ForwardStencilPass, 0.0f);
            }
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            base.CollectShaderProperties(collector, generationMode);

            collector.AddEnumProperty(PropertyNames.RenderQueue, (float) target.RenderQueue, typeof(RenderQueue),
                "Render Queue"
            );
            collector.AddEnumProperty(PropertyNames.QueueControl, (float) QueueControl.Auto, typeof(QueueControl),
                "Queue Control"
            );
            collector.AddFloatProperty(PropertyNames.QueueOffset, 0.0f);

            if (target.AllowMaterialOverride)
            {
                collector.AddFloatProperty(PropertyNames.CastShadows, target.CastShadows ? 1.0f : 0.0f);

                // setup properties using the defaults
                collector.AddFloatProperty(PropertyNames.SurfaceType, (float) target.SurfaceType);
                collector.AddFloatProperty(PropertyNames.BlendMode, (float) target.AlphaMode);
                collector.AddFloatProperty(PropertyNames.AlphaClipping, target.AlphaClip ? 1.0f : 0.0f);
                collector.AddFloatProperty(PropertyNames.ForceDisableFogPropertyName, !target.Fog ? 1.0f : 0.0f);
                collector.AddFloatProperty(PropertyNames.BlendSrc, 1.0f
                ); // always set by material inspector, ok to have incorrect values here
                collector.AddFloatProperty(PropertyNames.BlendDst, 0.0f
                ); // always set by material inspector, ok to have incorrect values here
                collector.AddToggleProperty(PropertyNames.ZWrite, target.SurfaceType == SurfaceType.Opaque);
                collector.AddFloatProperty(PropertyNames.ZWriteControl, (float) target.ZWriteControl);
                collector.AddFloatProperty(PropertyNames.ZTest, (float) target.ZTestMode
                ); // ztest mode is designed to directly pass as ztest
                collector.AddFloatProperty(PropertyNames.RenderFace, (float) target.RenderFace
                ); // render face enum is designed to directly pass as a cull mode
            }

            collector.AddFloatProperty(PropertyNames.ControlOutlinesStencilLayer,
                target.ControlOutlinesStencilLayerEffectivelyEnabled ? 1.0f : 0.0f
            );

            if (target.ControlOutlinesStencilLayerEffectivelyEnabled || target.AllowMaterialOverride)
            {
                collector.AddEnumProperty(PropertyNames.OutlinesStencilLayer, 0.0f, typeof(StencilLayer),
                    "Outlines Stencil Layer"
                );
                collector.AddFloatProperty(PropertyNames.ForwardStencilRef, 0.0f);
                collector.AddFloatProperty(PropertyNames.ForwardStencilWriteMask, 0.0f);
                collector.AddFloatProperty(PropertyNames.ForwardStencilComp, 0.0f);
                collector.AddFloatProperty(PropertyNames.ForwardStencilPass, 0.0f);
            }
        }

        protected static SubShaderDescriptor PostProcessSubShader(SubShaderDescriptor subShaderDescriptor) =>
            subShaderDescriptor;
    }

    internal static class SubShaderUtils
    {
        internal static void AddFloatProperty(this PropertyCollector collector, string referenceName,
            float defaultValue, HLSLDeclaration declarationType = HLSLDeclaration.DoNotDeclare)
        {
            collector.AddShaderProperty(new Vector1ShaderProperty
                {
                    floatType = FloatType.Default,
                    hidden = true,
                    overrideHLSLDeclaration = true,
                    hlslDeclarationOverride = declarationType,
                    value = defaultValue,
                    displayName = referenceName,
                    overrideReferenceName = referenceName,
                }
            );
        }

        internal static void AddEnumProperty(this PropertyCollector collector, string referenceName,
            float defaultValue, Type enumType, string displayName = null,
            HLSLDeclaration declarationType = HLSLDeclaration.DoNotDeclare)
        {
            collector.AddShaderProperty(new Vector1ShaderProperty
                {
                    floatType = FloatType.Enum,
                    hidden = true,
                    overrideHLSLDeclaration = true,
                    hlslDeclarationOverride = declarationType,
                    value = defaultValue,
                    displayName = displayName ?? referenceName,
                    overrideReferenceName = referenceName,
                    enumType = EnumType.CSharpEnum,
                    cSharpEnumType = enumType,
                }
            );
        }

        internal static void AddToggleProperty(this PropertyCollector collector, string referenceName,
            bool defaultValue, HLSLDeclaration declarationType = HLSLDeclaration.DoNotDeclare)
        {
            collector.AddShaderProperty(new BooleanShaderProperty
                {
                    value = defaultValue,
                    hidden = true,
                    overrideHLSLDeclaration = true,
                    hlslDeclarationOverride = declarationType,
                    displayName = referenceName,
                    overrideReferenceName = referenceName,
                }
            );
        }
    }
}