﻿#if UNITY_EDITOR || DEVELOPMENT_BUILD
#define SET_MATERIAL_NAME
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD

using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace DELTation.ToonRP
{
    public static class ToonRpUtils
    {
        [Conditional("SET_MATERIAL_NAME")]
        private static void SetMaterialName(Material material, string name)
        {
            material.name = name;
        }

        public static Material CreateEngineMaterial(Shader shader, string materialName)
        {
            Material material = CoreUtils.CreateEngineMaterial(shader);
            SetMaterialName(material, materialName);
            return material;
        }

        public static Material CreateEngineMaterial(string shaderPath, string materialName)
        {
            Material material = CoreUtils.CreateEngineMaterial(shaderPath);
            SetMaterialName(material, materialName);
            return material;
        }

        public static Material CreateEngineMaterial(Material source, string materialName)
        {
            var material = new Material(source)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            SetMaterialName(material, materialName);
            return material;
        }

        public static void ExecuteCommandBufferAndClear(ref this ScriptableRenderContext context, CommandBuffer cmd)
        {
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static void SetViewAndProjectionMatrices(CommandBuffer cmd, Matrix4x4 viewMatrix,
            Matrix4x4 gpuProjectionMatrix,
            bool setInverseMatrices)
        {
            Matrix4x4 viewAndProjectionMatrix = gpuProjectionMatrix * viewMatrix;
            cmd.SetGlobalMatrix(ShaderPropertyId.ViewMatrix, viewMatrix);
            cmd.SetGlobalMatrix(ShaderPropertyId.ProjectionMatrix, gpuProjectionMatrix);
            cmd.SetGlobalMatrix(ShaderPropertyId.ViewAndProjectionMatrix, viewAndProjectionMatrix);

            if (!setInverseMatrices)
            {
                return;
            }

            SetInverseViewAndProjectionMatrices(cmd, viewMatrix, gpuProjectionMatrix, viewAndProjectionMatrix);
        }

        public static void SetupCameraProperties(ref ScriptableRenderContext context,
            ToonAdditionalCameraData additionalCameraData,
            Matrix4x4 overrideProjectionMatrix)
        {
            additionalCameraData.SetCustomProjectionMatrix(overrideProjectionMatrix);
            context.SetupCameraProperties(additionalCameraData.Camera);
        }

        private static void SetInverseViewAndProjectionMatrices(CommandBuffer cmd,
            Matrix4x4 viewMatrix,
            Matrix4x4 gpuProjectionMatrix,
            Matrix4x4 viewAndProjectionMatrix
        )
        {
            var inverseViewMatrix = Matrix4x4.Inverse(viewMatrix);
            var inverseProjectionMatrix = Matrix4x4.Inverse(gpuProjectionMatrix);
            var inverseViewProjection = Matrix4x4.Inverse(viewAndProjectionMatrix);
            cmd.SetGlobalMatrix(ShaderPropertyId.InverseViewMatrix, inverseViewMatrix);
            cmd.SetGlobalMatrix(ShaderPropertyId.InverseProjectionMatrix, inverseProjectionMatrix);
            cmd.SetGlobalMatrix(ShaderPropertyId.InverseViewAndProjectionMatrix, inverseViewProjection);
        }

        public static Matrix4x4 GetGPUProjectionMatrix(Matrix4x4 projectionMatrix, bool renderIntoTexture) =>
            GL.GetGPUProjectionMatrix(projectionMatrix, SystemInfo.graphicsUVStartsAtTop && renderIntoTexture);

        public static Vector4 BuildRampVectorFromEdges(float edge1, float edge2) =>
            BuildRampVectorFromSmoothness(edge1, edge2 - edge1);

        public static Vector4 BuildRampVectorCrispAntiAliased(float edge1) => new(edge1, 0);

        public static Vector4 BuildRampVectorFromSmoothness(float threshold, float smoothness)
        {
            // saturate((v - a) * invBMinusA)
            float invBMinusA = 1.0f / Mathf.Max(smoothness, 0.0001f);
            // Transforming to the mad-compatible form (a * x + b): 
            // saturate(v * invBMinusA - a * invBMinusA) =>
            // saturate(v * invBMinusA + (-a * invBMinusA)) 
            return new Vector4(invBMinusA, -threshold * invBMinusA);
        }

        public static class ShaderPropertyId
        {
            public static readonly int ViewMatrix = Shader.PropertyToID("unity_MatrixV");
            public static readonly int ProjectionMatrix = Shader.PropertyToID("glstate_matrix_projection");
            public static readonly int ViewAndProjectionMatrix = Shader.PropertyToID("unity_MatrixVP");

            public static readonly int InverseViewMatrix = Shader.PropertyToID("unity_MatrixInvV");
            public static readonly int InverseProjectionMatrix = Shader.PropertyToID("unity_MatrixInvP");
            public static readonly int InverseViewAndProjectionMatrix = Shader.PropertyToID("unity_MatrixInvVP");
        }
    }
}