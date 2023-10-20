using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DELTation.ToonRP.Editor.ShaderGUI;
using UnityEditor;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Serialization;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace DELTation.ToonRP.Editor.ShaderGraph.Targets
{
    internal enum SurfaceType
    {
        Opaque,
        Transparent,
    }

    internal enum ZWriteControl
    {
        Auto = 0,
        ForceEnabled = 1,
        ForceDisabled = 2,
    }

    internal enum ZTestMode // the values here match UnityEngine.Rendering.CompareFunction
    {
        Disabled = 0,
        Never = 1,
        Less = 2,
        Equal = 3,
        LEqual = 4, // default for most rendering
        Greater = 5,
        NotEqual = 6,
        GEqual = 7,
        Always = 8,
    }

    internal enum AlphaMode
    {
        Alpha,
        Premultiply,
        Additive,
        Multiply,
    }

    internal enum RenderFace
    {
        Front = 2, // = CullMode.Back -- render front face only
        Back = 1, // = CullMode.Front -- render back face only
        Both = 0, // = CullMode.Off -- render both faces
    }

    internal sealed class ToonTarget : Target, IHasMetadata
    {
        public const string UberTemplatePath =
            "Packages/com.deltation.toon-rp/Editor/ShaderGraph/Templates/ShaderPass.template";

        // Constants
        private static readonly GUID SourceCodeGuid = new("5887ebecda26f434fbc73c8064f0525a"); // ToonTarget.cs
        public static readonly string[] SharedTemplateDirectories = GenerationUtils
            .GetDefaultSharedTemplateDirectories().Union(new[]
                {
                    "Packages/com.deltation.toon-rp/Editor/ShaderGraph/Templates",
                }
            ).ToArray();
        private readonly List<string> _subTargetNames;
        private TextField _customGUIField;

        // View
        private PopupField<string> _subTargetField;

        // SubTarget
        private List<SubTarget> _subTargets;

        public ToonTarget()
        {
            displayName = "Toon";
            _subTargets = TargetUtils.GetSubTargets(this);
            _subTargetNames = _subTargets.Select(x => x.displayName).ToList();
            TargetUtils.ProcessSubTargetList(ref _activeSubTarget, ref _subTargets);
        }

        public override int latestVersion => 1;
        private int ActiveSubTargetIndex => _subTargets.IndexOf(_activeSubTarget);

        internal override bool ignoreCustomInterpolators => false;
        internal override int padCustomInterpolatorLimit => 4;

        public string RenderType => SurfaceType == SurfaceType.Transparent
            ? $"{UnityEditor.ShaderGraph.RenderType.Transparent}"
            : $"{UnityEditor.ShaderGraph.RenderType.Opaque}";

        // this sets up the default renderQueue -- but it can be overridden by ResetMaterialKeywords()
        public string RenderQueue
        {
            get
            {
                if (SurfaceType == SurfaceType.Transparent)
                {
                    return $"{UnityEditor.ShaderGraph.RenderQueue.Transparent}";
                }

                return AlphaClip
                    ? $"{UnityEditor.ShaderGraph.RenderQueue.AlphaTest}"
                    : $"{UnityEditor.ShaderGraph.RenderQueue.Geometry}";
            }
        }

        public SubTarget ActiveSubTarget
        {
            get => _activeSubTarget.value;
            set => _activeSubTarget = value;
        }

        public bool AllowMaterialOverride
        {
            get => _allowMaterialOverride;
            private set => _allowMaterialOverride = value;
        }

        public SurfaceType SurfaceType
        {
            get => _surfaceType;
            private set => _surfaceType = value;
        }

        public ZWriteControl ZWriteControl
        {
            get => _zWriteControl;
            private set => _zWriteControl = value;
        }

        public ZTestMode ZTestMode
        {
            get => _zTestMode;
            private set => _zTestMode = value;
        }

        public AlphaMode AlphaMode
        {
            get => _alphaMode;
            private set => _alphaMode = value;
        }

        public RenderFace RenderFace
        {
            get => _renderFace;
            private set => _renderFace = value;
        }

        public bool AlphaClip
        {
            get => _alphaClip;
            private set => _alphaClip = value;
        }

        public bool CastShadows
        {
            get => _castShadows;
            private set => _castShadows = value;
        }

        public bool ReceiveShadows
        {
            get => _receiveShadows;
            private set => _receiveShadows = value;
        }

        private string CustomEditorGUI
        {
            get => _customEditorGUI;
            set => _customEditorGUI = value;
        }

        // generally used to know if we need to build a depth pass
        public bool MayWriteDepth
        {
            get
            {
                if (AllowMaterialOverride)
                {
                    // material may or may not choose to write depth... we should create the depth pass
                    return true;
                }

                return ZWriteControl switch
                {
                    ZWriteControl.Auto => SurfaceType == SurfaceType.Opaque,
                    ZWriteControl.ForceDisabled => false,
                    _ => true,
                };
            }
        }

        public override object saveContext => _activeSubTarget.value?.saveContext;

        public override bool IsActive()
        {
            bool isUniversalRenderPipeline = GraphicsSettings.currentRenderPipeline is ToonRenderPipelineAsset;
            return isUniversalRenderPipeline && ActiveSubTarget.IsActive();
        }

        public override bool IsNodeAllowedByTarget(Type nodeType)
        {
            SRPFilterAttribute srpFilter = NodeClassCache.GetAttributeOnNodeType<SRPFilterAttribute>(nodeType);
            bool worksWithThisSrp = srpFilter == null || srpFilter.srpTypes.Contains(typeof(ToonRenderPipeline));

            SubTargetFilterAttribute subTargetFilter =
                NodeClassCache.GetAttributeOnNodeType<SubTargetFilterAttribute>(nodeType);
            bool worksWithThisSubTarget = subTargetFilter == null ||
                                          subTargetFilter.subTargetTypes.Contains(ActiveSubTarget.GetType());

            return worksWithThisSrp && worksWithThisSubTarget && base.IsNodeAllowedByTarget(nodeType);
        }

        public override void Setup(ref TargetSetupContext context)
        {
            // Setup the Target
            context.AddAssetDependency(SourceCodeGuid, AssetCollection.Flags.SourceDependency);

            // Override EditorGUI (replaces the ToonRP material editor by a custom one)
            if (!string.IsNullOrEmpty(_customEditorGUI))
            {
                context.AddCustomEditorForRenderPipeline(_customEditorGUI, typeof(ToonRenderPipelineAsset));
            }

            // Setup the active SubTarget
            TargetUtils.ProcessSubTargetList(ref _activeSubTarget, ref _subTargets);
            _activeSubTarget.value.target = this;
            _activeSubTarget.value.Setup(ref context);
        }

        public override void OnAfterMultiDeserialize(string json)
        {
            TargetUtils.ProcessSubTargetList(ref _activeSubTarget, ref _subTargets);
            _activeSubTarget.value.target = this;
        }

        public override void GetFields(ref TargetFieldContext context)
        {
            BlockFieldDescriptor[] descs = context.blocks.Select(x => x.descriptor).ToArray();
            // Core fields
            context.AddField(Fields.GraphVertex, descs.Contains(ToonBlockFields.VertexDescription.Position) ||
                                                 descs.Contains(ToonBlockFields.VertexDescription.Normal) ||
                                                 descs.Contains(ToonBlockFields.VertexDescription.Tangent)
            );
            context.AddField(Fields.GraphPixel);

            // SubTarget fields
            _activeSubTarget.value.GetFields(ref context);
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            // Core blocks
            context.AddBlock(ToonBlockFields.VertexDescription.Position);
            context.AddBlock(ToonBlockFields.VertexDescription.Normal);
            context.AddBlock(ToonBlockFields.VertexDescription.Tangent);
            context.AddBlock(ToonBlockFields.SurfaceDescription.Albedo);

            // SubTarget blocks
            _activeSubTarget.value.GetActiveBlocks(ref context);
        }

        public override void ProcessPreviewMaterial(Material material)
        {
            _activeSubTarget.value.ProcessPreviewMaterial(material);
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            base.CollectShaderProperties(collector, generationMode);
            ActiveSubTarget.CollectShaderProperties(collector, generationMode);

            // SubTarget blocks
            _activeSubTarget.value.CollectShaderProperties(collector, generationMode);
        }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange,
            Action<string> registerUndo)
        {
            // Core properties
            _subTargetField = new PopupField<string>(_subTargetNames, ActiveSubTargetIndex);
            context.AddProperty("Material", _subTargetField, _ =>
                {
                    if (Equals(ActiveSubTargetIndex, _subTargetField.index))
                    {
                        return;
                    }

                    registerUndo("Change Material");
                    _activeSubTarget = _subTargets[_subTargetField.index];
                    onChange();
                }
            );

            // SubTarget properties
            _activeSubTarget.value.GetPropertiesGUI(ref context, onChange, registerUndo);

            // Custom Editor GUI
            // Requires FocusOutEvent
            _customGUIField = new TextField("") { value = CustomEditorGUI };
            _customGUIField.RegisterCallback<FocusOutEvent>(_ =>
                {
                    if (Equals(CustomEditorGUI, _customGUIField.value))
                    {
                        return;
                    }

                    registerUndo("Change Custom Editor GUI");
                    CustomEditorGUI = _customGUIField.value;
                    onChange();
                }
            );
            context.AddProperty("Custom Editor GUI", _customGUIField, _ => { });
        }

        public void AddDefaultMaterialOverrideGUI(ref TargetPropertyGUIContext context, Action onChange,
            Action<string> registerUndo)
        {
            // At some point we may want to convert this to be a per-property control
            // or Unify the UX with the upcoming "lock" feature of the Material Variant properties
            context.AddProperty("Allow Material Override", new Toggle { value = AllowMaterialOverride }, evt =>
                {
                    if (Equals(AllowMaterialOverride, evt.newValue))
                    {
                        return;
                    }

                    registerUndo("Change Allow Material Override");
                    AllowMaterialOverride = evt.newValue;
                    onChange();
                }
            );
        }

        public void AddDefaultSurfacePropertiesGUI(ref TargetPropertyGUIContext context, Action onChange,
            Action<string> registerUndo, bool showReceiveShadows)
        {
            context.AddProperty("Surface Type", new EnumField(SurfaceType.Opaque) { value = SurfaceType }, evt =>
                {
                    if (Equals(SurfaceType, evt.newValue))
                    {
                        return;
                    }

                    registerUndo("Change Surface");
                    SurfaceType = (SurfaceType) evt.newValue;
                    onChange();
                }
            );

            context.AddProperty("Blending Mode", new EnumField(AlphaMode.Alpha) { value = AlphaMode },
                SurfaceType == SurfaceType.Transparent, evt =>
                {
                    if (Equals(AlphaMode, evt.newValue))
                    {
                        return;
                    }

                    registerUndo("Change Blend");
                    AlphaMode = (AlphaMode) evt.newValue;
                    onChange();
                }
            );

            context.AddProperty("Render Face", new EnumField(RenderFace.Front) { value = RenderFace }, evt =>
                {
                    if (Equals(RenderFace, evt.newValue))
                    {
                        return;
                    }

                    registerUndo("Change Render Face");
                    RenderFace = (RenderFace) evt.newValue;
                    onChange();
                }
            );

            context.AddProperty("Depth Write", new EnumField(ZWriteControl.Auto) { value = ZWriteControl }, evt =>
                {
                    if (Equals(ZWriteControl, evt.newValue))
                    {
                        return;
                    }

                    registerUndo("Change Depth Write Control");
                    ZWriteControl = (ZWriteControl) evt.newValue;
                    onChange();
                }
            );

            context.AddProperty("Depth Test",
                new EnumField(ZTestModeForUI.LEqual) { value = (ZTestModeForUI) ZTestMode }, evt =>
                {
                    if (Equals(ZTestMode, evt.newValue))
                    {
                        return;
                    }

                    registerUndo("Change Depth Test");
                    ZTestMode = (ZTestMode) evt.newValue;
                    onChange();
                }
            );

            context.AddProperty("Alpha Clipping", new Toggle { value = AlphaClip }, evt =>
                {
                    if (Equals(AlphaClip, evt.newValue))
                    {
                        return;
                    }

                    registerUndo("Change Alpha Clip");
                    AlphaClip = evt.newValue;
                    onChange();
                }
            );

            context.AddProperty("Cast Shadows", new Toggle { value = CastShadows }, evt =>
                {
                    if (Equals(CastShadows, evt.newValue))
                    {
                        return;
                    }

                    registerUndo("Change Cast Shadows");
                    CastShadows = evt.newValue;
                    onChange();
                }
            );

            if (showReceiveShadows)
            {
                context.AddProperty("Receive Shadows", new Toggle { value = ReceiveShadows }, evt =>
                    {
                        if (Equals(ReceiveShadows, evt.newValue))
                        {
                            return;
                        }

                        registerUndo("Change Receive Shadows");
                        ReceiveShadows = evt.newValue;
                        onChange();
                    }
                );
            }
        }

        public bool TrySetActiveSubTarget(Type subTargetType)
        {
            if (!subTargetType.IsSubclassOf(typeof(SubTarget)))
            {
                return false;
            }

            foreach (SubTarget subTarget in _subTargets)
            {
                if (subTarget.GetType() == subTargetType)
                {
                    _activeSubTarget = subTarget;
                    return true;
                }
            }

            return false;
        }

        public override bool WorksWithSRP(RenderPipelineAsset scriptableRenderPipeline) =>
            // ReSharper disable once Unity.NoNullPropagation
            scriptableRenderPipeline?.GetType() == typeof(ToonRenderPipelineAsset);

        public override void OnAfterDeserialize(string json)
        {
            base.OnAfterDeserialize(json);

            if (sgVersion < latestVersion)
            {
                ChangeVersion(latestVersion);
            }
        }

        // this is a copy of ZTestMode, but hides the "Disabled" option, which is invalid
        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        private enum ZTestModeForUI
        {
            Never = 1,
            Less = 2,
            Equal = 3,
            LEqual = 4, // default for most rendering
            Greater = 5,
            NotEqual = 6,
            GEqual = 7,
            Always = 8,
        }

        // ReSharper disable Unity.RedundantSerializeFieldAttribute
        [SerializeField] private JsonData<SubTarget> _activeSubTarget;
        // when checked, allows the material to control ALL surface settings (uber shader style)
        [SerializeField] private bool _allowMaterialOverride;
        [SerializeField] private SurfaceType _surfaceType = SurfaceType.Opaque;
        [SerializeField] private ZTestMode _zTestMode = ZTestMode.LEqual;
        [SerializeField] private ZWriteControl _zWriteControl = ZWriteControl.Auto;
        [SerializeField] private AlphaMode _alphaMode = AlphaMode.Alpha;
        [SerializeField] private RenderFace _renderFace = RenderFace.Front;
        [SerializeField] private bool _alphaClip;
        [SerializeField] private bool _castShadows = true;
        [SerializeField] private bool _receiveShadows = true;
        [SerializeField] private string _customEditorGUI;
        // ReSharper restore Unity.RedundantSerializeFieldAttribute

        #region Metadata

        string IHasMetadata.identifier
        {
            get
            {
                // defer to subtarget
                if (_activeSubTarget.value is IHasMetadata subTargetHasMetaData)
                {
                    return subTargetHasMetaData.identifier;
                }

                return null;
            }
        }

        ScriptableObject IHasMetadata.GetMetadataObject(GraphDataReadOnly graph)
        {
            // defer to subtarget
            if (_activeSubTarget.value is IHasMetadata subTargetHasMetaData)
            {
                return subTargetHasMetaData.GetMetadataObject(graph);
            }

            return null;
        }

        #endregion
    }

    #region Passes

    internal static class CorePasses
    {
        private static void AddAlphaClipControlToPass(ref PassDescriptor pass, ToonTarget target)
        {
            if (target.AllowMaterialOverride)
            {
                pass.keywords.Add(CoreKeywordDescriptors.AlphaTestOn);
            }
            else if (target.AlphaClip)
            {
                pass.defines.Add(CoreKeywordDescriptors.AlphaTestOn, 1);
            }
        }

        internal static void AddTargetSurfaceControlsToPass(ref PassDescriptor pass, ToonTarget target)
        {
            // the surface settings can either be material controlled or target controlled
            if (target.AllowMaterialOverride)
            {
                // setup material control of via keyword
                pass.keywords.Add(CoreKeywordDescriptors.SurfaceTypeTransparent);
                pass.keywords.Add(CoreKeywordDescriptors.AlphaPremultiplyOn);
            }
            else
            {
                // setup target control via define
                if (target.SurfaceType == SurfaceType.Transparent)
                {
                    pass.defines.Add(CoreKeywordDescriptors.SurfaceTypeTransparent, 1);
                }

                if (target.AlphaMode == AlphaMode.Premultiply)
                {
                    pass.defines.Add(CoreKeywordDescriptors.AlphaPremultiplyOn, 1);
                }
            }

            AddAlphaClipControlToPass(ref pass, target);
        }

        public static PassDescriptor DepthOnly(ToonTarget target)
        {
            ref readonly ToonPasses.Pass pass = ref ToonPasses.DepthOnly;
            var result = new PassDescriptor
            {
                // Definition
                displayName = pass.Name,
                referenceName = pass.ReferenceName,
                lightMode = pass.LightMode,
                useInPreview = true,

                // Template
                passTemplatePath = ToonTarget.UberTemplatePath,
                sharedTemplateDirectories = ToonTarget.SharedTemplateDirectories,

                // Port Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = CoreBlockMasks.FragmentAlphaOnly,

                // Fields
                structs = CoreStructCollections.Default,
                fieldDependencies = CoreFieldDependencies.Default,

                // Conditional State
                renderStates = CoreRenderStates.DepthOnly(target),
                pragmas = CorePragmas.Instanced,
                defines = new DefineCollection(),
                keywords = new KeywordCollection(),
                includes = CoreIncludes.DepthOnly,

                // Custom Interpolator Support
                customInterpolators = CoreCustomInterpDescriptors.Common,
            };

            AddAlphaClipControlToPass(ref result, target);

            return result;
        }

        public static PassDescriptor DepthNormals(ToonTarget target)
        {
            ref readonly ToonPasses.Pass pass = ref ToonPasses.DepthNormals;
            var result = new PassDescriptor
            {
                // Definition
                displayName = pass.Name,
                referenceName = pass.ReferenceName,
                lightMode = pass.LightMode,
                useInPreview = false,

                // Template
                passTemplatePath = ToonTarget.UberTemplatePath,
                sharedTemplateDirectories = ToonTarget.SharedTemplateDirectories,

                // Port Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = CoreBlockMasks.FragmentDepthNormals,

                // Fields
                structs = CoreStructCollections.Default,
                requiredFields = CoreRequiredFields.DepthNormals,
                fieldDependencies = CoreFieldDependencies.Default,

                // Conditional State
                renderStates = CoreRenderStates.DepthNormals(target),
                pragmas = CorePragmas.Instanced,
                defines = new DefineCollection(),
                keywords = new KeywordCollection(),
                includes = CoreIncludes.DepthNormals,

                // Custom Interpolator Support
                customInterpolators = CoreCustomInterpDescriptors.Common,
            };

            AddAlphaClipControlToPass(ref result, target);

            return result;
        }

        public static PassDescriptor MotionVectors(ToonTarget target)
        {
            ref readonly ToonPasses.Pass pass = ref ToonPasses.MotionVectors;
            var result = new PassDescriptor
            {
                // Definition
                displayName = pass.Name,
                referenceName = pass.ReferenceName,
                lightMode = pass.LightMode,
                useInPreview = false,

                // Template
                passTemplatePath = ToonTarget.UberTemplatePath,
                sharedTemplateDirectories = ToonTarget.SharedTemplateDirectories,

                // Port Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = CoreBlockMasks.MotionVectors,

                // Fields
                structs = CoreStructCollections.Default,
                requiredFields = CoreRequiredFields.MotionVectors,
                fieldDependencies = CoreFieldDependencies.Default,

                // Conditional State
                renderStates = CoreRenderStates.MotionVectors(target),
                pragmas = CorePragmas.Instanced,
                defines = new DefineCollection(),
                keywords = new KeywordCollection(),
                includes = CoreIncludes.MotionVectors,

                // Custom Interpolator Support
                customInterpolators = CoreCustomInterpDescriptors.Common,
            };

            AddAlphaClipControlToPass(ref result, target);

            return result;
        }

        public static PassDescriptor ShadowCaster(ToonTarget target)
        {
            ref readonly ToonPasses.Pass pass = ref ToonPasses.ShadowCaster;
            var result = new PassDescriptor
            {
                // Definition
                displayName = pass.Name,
                referenceName = pass.ReferenceName,
                lightMode = pass.LightMode,

                // Template
                passTemplatePath = ToonTarget.UberTemplatePath,
                sharedTemplateDirectories = ToonTarget.SharedTemplateDirectories,

                // Port Mask
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = CoreBlockMasks.FragmentAlphaOnly,

                // Fields
                structs = CoreStructCollections.Default,
                requiredFields = CoreRequiredFields.ShadowCaster,
                fieldDependencies = CoreFieldDependencies.Default,

                // Conditional State
                renderStates = CoreRenderStates.ShadowCaster(target),
                pragmas = CorePragmas.Instanced,
                defines = new DefineCollection(),
                keywords = DefaultKeywords.ShadowCaster,
                includes = CoreIncludes.ShadowCaster,

                // Custom Interpolator Support
                customInterpolators = CoreCustomInterpDescriptors.Common,
            };

            AddAlphaClipControlToPass(ref result, target);

            return result;
        }
    }

    #endregion

    #region PortMasks

    internal static class CoreBlockMasks
    {
        public static readonly BlockFieldDescriptor[] Vertex =
        {
            ToonBlockFields.VertexDescription.Position,
            ToonBlockFields.VertexDescription.Normal,
            ToonBlockFields.VertexDescription.Tangent,
        };

        public static readonly BlockFieldDescriptor[] FragmentAlphaOnly =
        {
            ToonBlockFields.SurfaceDescription.Alpha,
            ToonBlockFields.SurfaceDescription.AlphaClipThreshold,
        };

        public static readonly BlockFieldDescriptor[] FragmentColorAlpha =
        {
            ToonBlockFields.SurfaceDescription.Albedo,
            ToonBlockFields.SurfaceDescription.Alpha,
            ToonBlockFields.SurfaceDescription.AlphaClipThreshold,
        };

        public static readonly BlockFieldDescriptor[] FragmentDepthNormals =
        {
            ToonBlockFields.SurfaceDescription.NormalOs,
            ToonBlockFields.SurfaceDescription.NormalTs,
            ToonBlockFields.SurfaceDescription.NormalWs,
            ToonBlockFields.SurfaceDescription.Alpha,
            ToonBlockFields.SurfaceDescription.AlphaClipThreshold,
        };

        public static readonly BlockFieldDescriptor[] MotionVectors =
        {
            ToonBlockFields.SurfaceDescription.Alpha,
            ToonBlockFields.SurfaceDescription.AlphaClipThreshold,
        };
    }

    #endregion

    #region StructCollections

    internal static class CoreStructCollections
    {
        public static readonly StructCollection Default = new()
        {
            ToonStructs.Attributes,
            ToonStructs.Varyings,
            Structs.SurfaceDescriptionInputs,
            Structs.VertexDescriptionInputs,
        };
    }

    #endregion

    #region RequiredFields

    internal static class CoreRequiredFields
    {
        public static readonly FieldCollection ShadowCaster = new()
        {
            ToonStructFields.Varyings.vsmDepth,
        };

        public static readonly FieldCollection DepthNormals = new()
        {
            StructFields.Varyings.normalWS,
            StructFields.Varyings.tangentWS,
        };
        public static readonly FieldCollection MotionVectors = new()
        {
            ToonStructFields.Attributes.positionOld,
            ToonStructFields.Varyings.positionCsNoJitter,
            ToonStructFields.Varyings.previousPositionCsNoJitter,
        };
    }

    #endregion

    #region Keywords

    internal static class DefaultKeywords
    {
        public static readonly KeywordCollection ShadowCaster = new()
        {
            CoreKeywordDescriptors.ToonRpVsmShadowCaster,
        };
    }

    #endregion

    #region FieldDependencies

    internal static class CoreFieldDependencies
    {
        public static readonly DependencyCollection Default = new()
        {
            ToonFieldDependencies.Default,
        };
    }

    #endregion

    #region RenderStates

    internal static class CoreRenderStates
    {
        private static readonly RenderStateCollection MaterialControlledRenderState = new()
        {
            RenderState.ZTest(Uniforms.ZTest),
            RenderState.ZWrite(Uniforms.ZWrite),
            RenderState.Cull(Uniforms.CullMode),
            RenderState.Blend(Uniforms.SrcBlend, Uniforms.DstBlend
            ), //, Uniforms.alphaSrcBlend, Uniforms.alphaDstBlend) },
        };

        private static Cull RenderFaceToCull(RenderFace renderFace) =>
            renderFace switch
            {
                RenderFace.Back => Cull.Front,
                RenderFace.Front => Cull.Back,
                RenderFace.Both => Cull.Off,
                _ => Cull.Back,
            };

        public static RenderStateCollection UberSwitchedRenderState(ToonTarget target)
        {
            if (target.AllowMaterialOverride)
            {
                return MaterialControlledRenderState;
            }

            var result = new RenderStateCollection
            {
                RenderState.ZTest(target.ZTestMode.ToString()),
            };

            switch (target.ZWriteControl)
            {
                case ZWriteControl.Auto:
                    result.Add(target.SurfaceType == SurfaceType.Opaque
                        ? RenderState.ZWrite(ZWrite.On)
                        : RenderState.ZWrite(ZWrite.Off)
                    );
                    break;
                case ZWriteControl.ForceEnabled:
                    result.Add(RenderState.ZWrite(ZWrite.On));
                    break;
                case ZWriteControl.ForceDisabled:
                default:
                    result.Add(RenderState.ZWrite(ZWrite.Off));
                    break;
            }

            result.Add(RenderState.Cull(RenderFaceToCull(target.RenderFace)));

            if (target.SurfaceType == SurfaceType.Opaque)
            {
                result.Add(RenderState.Blend(Blend.One, Blend.Zero));
            }
            else
            {
                switch (target.AlphaMode)
                {
                    case AlphaMode.Alpha:
                        result.Add(RenderState.Blend(Blend.SrcAlpha, Blend.OneMinusSrcAlpha, Blend.One,
                                Blend.OneMinusSrcAlpha
                            )
                        );
                        break;
                    case AlphaMode.Premultiply:
                        result.Add(RenderState.Blend(Blend.One, Blend.OneMinusSrcAlpha, Blend.One,
                                Blend.OneMinusSrcAlpha
                            )
                        );
                        break;
                    case AlphaMode.Additive:
                        result.Add(RenderState.Blend(Blend.SrcAlpha, Blend.One, Blend.One, Blend.One));
                        break;
                    case AlphaMode.Multiply:
                        result.Add(RenderState.Blend(Blend.DstColor, Blend.Zero));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return result;
        }

        private static RenderStateDescriptor UberSwitchedCullRenderState(ToonTarget target) =>
            target.AllowMaterialOverride
                ? RenderState.Cull(Uniforms.CullMode)
                : RenderState.Cull(RenderFaceToCull(target.RenderFace));

        public static RenderStateCollection ShadowCaster(ToonTarget target)
        {
            var result = new RenderStateCollection
            {
                RenderState.ZTest(ZTest.LEqual),
                RenderState.ZWrite(ZWrite.On),
                UberSwitchedCullRenderState(target),
                RenderState.ColorMask("ColorMask RG"),
            };
            return result;
        }

        public static RenderStateCollection DepthOnly(ToonTarget target)
        {
            var result = new RenderStateCollection
            {
                RenderState.ZTest(ZTest.LEqual),
                RenderState.ZWrite(ZWrite.On),
                UberSwitchedCullRenderState(target),
                RenderState.ColorMask("ColorMask 0"),
            };

            return result;
        }

        public static RenderStateCollection DepthNormals(ToonTarget target)
        {
            var result = new RenderStateCollection
            {
                RenderState.ZTest(ZTest.LEqual),
                RenderState.ZWrite(ZWrite.On),
                UberSwitchedCullRenderState(target),
                RenderState.ColorMask("ColorMask RGB"),
            };

            return result;
        }

        public static RenderStateCollection MotionVectors(ToonTarget target)
        {
            var result = new RenderStateCollection
            {
                RenderState.ZTest(ZTest.LEqual),
                RenderState.ZWrite(ZWrite.On),
                UberSwitchedCullRenderState(target),
                RenderState.ColorMask("ColorMask RG"),
            };

            return result;
        }

        private static class Uniforms
        {
            public static readonly string SrcBlend = "[" + PropertyNames.BlendSrc + "]";
            public static readonly string DstBlend = "[" + PropertyNames.BlendDst + "]";
            public static readonly string CullMode = "[" + PropertyNames.RenderFace + "]";
            public static readonly string ZWrite = "[" + PropertyNames.ZWrite + "]";
            public static readonly string ZTest = "[" + PropertyNames.ZTest + "]";
        }
    }

    #endregion

    #region Pragmas

    internal static class CorePragmas
    {
        private static readonly PragmaDescriptor VS = Pragma.Vertex("VS");
        private static readonly PragmaDescriptor PS = Pragma.Fragment("PS");

        public static readonly PragmaCollection Default = new()
        {
            Pragma.Target(ShaderModel.Target20),
            VS,
            PS,
        };

        public static readonly PragmaCollection Instanced = new()
        {
            Pragma.Target(ShaderModel.Target20),
            Pragma.MultiCompileInstancing,
            VS,
            PS,
        };

        public static readonly PragmaCollection Forward = new()
        {
            Pragma.Target(ShaderModel.Target35),
            Pragma.MultiCompileInstancing,
            Pragma.MultiCompileFog,
            Pragma.InstancingOptions(InstancingOptions.RenderingLayer),
            VS,
            PS,
        };
    }

    #endregion

    #region Includes

    internal static class CoreIncludes
    {
        private const string CoreColor = "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl";
        private const string CoreTexture = "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl";
        private const string Common = "Packages/com.deltation.toon-rp/ShaderLibrary/Common.hlsl";
        private const string Lighting = "Packages/com.deltation.toon-rp/ShaderLibrary/Lighting.hlsl";
        private const string Shadows = "Packages/com.deltation.toon-rp/ShaderLibrary/Shadows.hlsl";
        private const string Textures = "Packages/com.deltation.toon-rp/ShaderLibrary/Textures.hlsl";
        private const string GraphFunctions = "Packages/com.deltation.toon-rp/ShaderLibrary/ShaderGraphFunctions.hlsl";
        private const string Varyings =
            "Packages/com.deltation.toon-rp/Editor/ShaderGraph/Includes/Varyings.hlsl";
        private const string ShaderPass =
            "Packages/com.deltation.toon-rp/Editor/ShaderGraph/Includes/ShaderPass.hlsl";
        private const string DepthOnlyPass =
            "Packages/com.deltation.toon-rp/Editor/ShaderGraph/Includes/DepthOnlyPass.hlsl";
        private const string DepthNormalsPass =
            "Packages/com.deltation.toon-rp/Editor/ShaderGraph/Includes/DepthNormalsPass.hlsl";
        private const string MotionVectorsPass =
            "Packages/com.deltation.toon-rp/Editor/ShaderGraph/Includes/MotionVectorsPass.hlsl";
        private const string ShadowCasterPass =
            "Packages/com.deltation.toon-rp/Editor/ShaderGraph/Includes/ShadowCasterPass.hlsl";

        public static readonly IncludeCollection CorePregraph = new()
        {
            { CoreColor, IncludeLocation.Pregraph },
            { CoreTexture, IncludeLocation.Pregraph },
            { Common, IncludeLocation.Pregraph },
            { Lighting, IncludeLocation.Pregraph },
            { Shadows, IncludeLocation.Pregraph },
            { Textures, IncludeLocation.Pregraph },
        };

        public static readonly IncludeCollection ShaderGraphPregraph = new()
        {
            { GraphFunctions, IncludeLocation.Pregraph },
        };

        public static readonly IncludeCollection CorePostgraph = new()
        {
            { ShaderPass, IncludeLocation.Pregraph },
            { Varyings, IncludeLocation.Postgraph },
        };

        public static readonly IncludeCollection DepthOnly = new()
        {
            // Pre-graph
            CorePregraph,
            ShaderGraphPregraph,

            // Post-graph
            CorePostgraph,
            { DepthOnlyPass, IncludeLocation.Postgraph },
        };

        public static readonly IncludeCollection DepthNormals = new()
        {
            // Pre-graph
            CorePregraph,
            ShaderGraphPregraph,

            // Post-graph
            CorePostgraph,
            { DepthNormalsPass, IncludeLocation.Postgraph },
        };

        public static readonly IncludeCollection MotionVectors = new()
        {
            // Pre-graph
            CorePregraph,
            ShaderGraphPregraph,

            // Post-graph
            CorePostgraph,
            { MotionVectorsPass, IncludeLocation.Postgraph },
        };

        public static readonly IncludeCollection ShadowCaster = new()
        {
            // Pre-graph
            CorePregraph,
            ShaderGraphPregraph,

            // Post-graph
            CorePostgraph,
            { ShadowCasterPass, IncludeLocation.Postgraph },
        };
    }

    #endregion

    #region KeywordDescriptors

    internal static class CoreKeywordDescriptors
    {
        public static readonly KeywordDescriptor AlphaTestOn = new()
        {
            displayName = ShaderKeywords.AlphaTestOn,
            referenceName = ShaderKeywords.AlphaTestOn,
            type = KeywordType.Boolean,
            definition = KeywordDefinition.ShaderFeature,
            scope = KeywordScope.Local,
            stages = KeywordShaderStage.Fragment,
        };

        public static readonly KeywordDescriptor AlphaPremultiplyOn = new()
        {
            displayName = ShaderKeywords.AlphaPremultiplyOn,
            referenceName = ShaderKeywords.AlphaPremultiplyOn,
            type = KeywordType.Boolean,
            definition = KeywordDefinition.ShaderFeature,
            scope = KeywordScope.Local,
            stages = KeywordShaderStage.Fragment,
        };
        
        public static readonly KeywordDescriptor SurfaceTypeTransparent = new()
        {
            displayName = ShaderKeywords.SurfaceTypeTransparent,
            referenceName = ShaderKeywords.SurfaceTypeTransparent,
            type = KeywordType.Boolean,
            definition = KeywordDefinition.ShaderFeature,
            scope = KeywordScope.Local,
            stages = KeywordShaderStage.Fragment,
        };

        public static readonly KeywordDescriptor ToonRpVsmShadowCaster = new()
        {
            displayName = "Toon RP VSM",
            referenceName = ShaderKeywords.ToonRpVsm,
            type = KeywordType.Boolean,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
        };

        // // Global Ramp
        // + #pragma multi_compile_fragment _ _TOON_RP_GLOBAL_RAMP_CRISP _TOON_RP_GLOBAL_RAMP_TEXTURE
        //
        // // Shadows
        // + #pragma multi_compile _ _TOON_RP_DIRECTIONAL_SHADOWS _TOON_RP_DIRECTIONAL_CASCADED_SHADOWS _TOON_RP_BLOB_SHADOWS
        // + #pragma multi_compile_fragment _ _TOON_RP_PCF _TOON_RP_VSM
        // + #pragma multi_compile_fragment _ _TOON_RP_POISSON_SAMPLING_STRATIFIED _TOON_RP_POISSON_SAMPLING_ROTATED
        // + #pragma multi_compile_fragment _ _TOON_RP_POISSON_SAMPLING_EARLY_BAIL
        // + #pragma multi_compile_fragment _ _TOON_RP_SHADOWS_RAMP_CRISP
        // + #pragma multi_compile_fragment _ _TOON_RP_SHADOWS_PATTERN
        //
        // // Lights
        // + #pragma multi_compile _ _TOON_RP_ADDITIONAL_LIGHTS _TOON_RP_ADDITIONAL_LIGHTS_VERTEX
        // + #pragma multi_compile _ _TOON_RP_TILED_LIGHTING
        //
        // // SSAO
        // + #pragma multi_compile_fragment _ _TOON_RP_SSAO _TOON_RP_SSAO_PATTERN
        
        public static readonly KeywordDescriptor ToonRpGlobalRamp = new()
        {
            displayName = "Toon RP Global Ramp",
            referenceName = "",
            type = KeywordType.Enum,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            stages = KeywordShaderStage.Fragment,
            entries = new KeywordEntry[]
            {
                new() { displayName = "Default", referenceName = "" },
                new() { displayName = "Crisp", referenceName = "TOON_RP_GLOBAL_RAMP_CRISP" },
                new() { displayName = "Texture", referenceName = "TOON_RP_GLOBAL_RAMP_TEXTURE" },
            },
        };

        public static readonly KeywordDescriptor ToonRpDirectionalShadows = new()
        {
            displayName = "Toon RP Directional Shadows",
            referenceName = "",
            type = KeywordType.Enum,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            entries = new KeywordEntry[]
            {
                new() { displayName = "Off", referenceName = "" },
                new() { displayName = "No Cascade", referenceName = "TOON_RP_DIRECTIONAL_SHADOWS" },
                new() { displayName = "Cascade", referenceName = "TOON_RP_DIRECTIONAL_CASCADED_SHADOWS" },
                new() { displayName = "Blob", referenceName = "TOON_RP_BLOB_SHADOWS" },
            },
        };
        
        public static readonly KeywordDescriptor ToonRpShadowSmoothingMode = new()
        {
            displayName = "Toon RP Shadow Smoothing Mode",
            referenceName = "",
            type = KeywordType.Enum,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            stages = KeywordShaderStage.Fragment,
            entries = new KeywordEntry[]
            {
                new() { displayName = "Default", referenceName = "" },
                new() { displayName = "PCF", referenceName = "TOON_RP_PCF" },
                new() { displayName = "VSM", referenceName = "TOON_RP_VSM" },
            },
        };
        
        public static readonly KeywordDescriptor ToonRpPoissonSamplingMode = new()
        {
            displayName = "Toon RP Poisson Sampling Mode",
            referenceName = "",
            type = KeywordType.Enum,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            stages = KeywordShaderStage.Fragment,
            entries = new KeywordEntry[]
            {
                new() { displayName = "Default", referenceName = "" },
                new() { displayName = "Stratified", referenceName = "TOON_RP_POISSON_SAMPLING_STRATIFIED" },
                new() { displayName = "Rotated", referenceName = "TOON_RP_POISSON_SAMPLING_ROTATED" },
            },
        };
        
        public static readonly KeywordDescriptor ToonRpPoissonSamplingEarlyBail = new()
        {
            displayName = "Toon RP Poisson Sampling Early Bail",
            referenceName = "_TOON_RP_POISSON_SAMPLING_EARLY_BAIL",
            type = KeywordType.Boolean,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            stages = KeywordShaderStage.Fragment,
        };
        
        public static readonly KeywordDescriptor ToonRpShadowsRampCrisp = new()
        {
            displayName = "Toon RP Shadows Ramp Crisp",
            referenceName = "_TOON_RP_SHADOWS_RAMP_CRISP",
            type = KeywordType.Boolean,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            stages = KeywordShaderStage.Fragment,
        };
        
        public static readonly KeywordDescriptor ToonRpShadowsPattern = new()
        {
            displayName = "Toon RP Shadows Pattern",
            referenceName = "_TOON_RP_SHADOWS_PATTERN",
            type = KeywordType.Boolean,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            stages = KeywordShaderStage.Fragment,
        };
        
        public static readonly KeywordDescriptor ToonRpAdditionalLights = new()
        {
            displayName = "Toon RP Additional Lights",
            referenceName = "",
            type = KeywordType.Enum,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            entries = new KeywordEntry[]
            {
                new() { displayName = "Off", referenceName = "" },
                new() { displayName = "Per Pixel", referenceName = "TOON_RP_ADDITIONAL_LIGHTS" },
                new() { displayName = "Per Vertex", referenceName = "TOON_RP_ADDITIONAL_LIGHTS_PER_VERTEX" },
            },
        };
        
        public static readonly KeywordDescriptor ToonRpTiledLighting = new()
        {
            displayName = "Toon RP Tiled Lighting",
            referenceName = "_TOON_RP_TILED_LIGHTING",
            type = KeywordType.Boolean,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
        };
        
        public static readonly KeywordDescriptor ToonRpSsao = new()
        {
            displayName = "Toon RP SSAO",
            referenceName = "",
            type = KeywordType.Enum,
            definition = KeywordDefinition.MultiCompile,
            scope = KeywordScope.Global,
            stages = KeywordShaderStage.Fragment,
            entries = new KeywordEntry[]
            {
                new() { displayName = "Off", referenceName = "" },
                new() { displayName = "Default", referenceName = "TOON_RP_SSAO" },
                new() { displayName = "Pattern", referenceName = "TOON_RP_SSAO_PATTERN" },
            },
        };
    }

    #endregion

    #region CustomInterpolators

    internal static class CoreCustomInterpDescriptors
    {
        public static readonly CustomInterpSubGen.Collection Common = new()
        {
            // Custom interpolators are not explicitly defined in the SurfaceDescriptionInputs template.
            // This entry point will let us generate a block of pass-through assignments for each field.
            CustomInterpSubGen.Descriptor.MakeBlock(CustomInterpSubGen.Splice.k_spliceCopyToSDI, "output", "input"),

            // sgci_PassThroughFunc is called from BuildVaryings in Varyings.hlsl to copy custom interpolators from vertex descriptions.
            // this entry point allows for the function to be defined before it is used.
            CustomInterpSubGen.Descriptor.MakeFunc(CustomInterpSubGen.Splice.k_splicePreSurface,
                "CustomInterpolatorPassThroughFunc", "Varyings", "VertexDescription",
                "CUSTOMINTERPOLATOR_VARYPASSTHROUGH_FUNC", "FEATURES_GRAPH_VERTEX"
            ),
        };
    }

    #endregion
}