using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using Object = UnityEngine.Object;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class VrCameraPostProcessingSynchronizer
    {
        private readonly List<Object> ownedObjects = new List<Object>();

        public void Sync(Camera sourceCamera, Camera leftEye, Camera rightEye, VrRuntimeSettings settings)
        {
            if (sourceCamera == null || settings == null || !settings.EnableVrCameraPostProcessing)
            {
                Clear();
                return;
            }

            HashSet<string> whitelist = BuildWhitelist(settings.VrCameraPostProcessingWhitelist);
            HashSet<string> pp2Whitelist = BuildWhitelist(settings.VrPp2EffectWhitelist);
            if (whitelist.Count == 0 && pp2Whitelist.Count == 0)
            {
                Clear();
                return;
            }

            if (!HasWhitelistedPostProcessing(sourceCamera, whitelist, pp2Whitelist))
            {
                return;
            }

            Clear();
            CopyWhitelistedComponents(sourceCamera, leftEye, whitelist);
            CopyWhitelistedComponents(sourceCamera, rightEye, whitelist);
            CopyFilteredPostProcessVolumes(sourceCamera, leftEye, settings, pp2Whitelist);
            CopyFilteredPostProcessVolumes(sourceCamera, rightEye, settings, pp2Whitelist);
        }

        public void Clear()
        {
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
            {
                Object ownedObject = ownedObjects[i];
                if (ownedObject != null)
                {
                    Object.DestroyImmediate(ownedObject);
                }
            }

            ownedObjects.Clear();
        }

        private int CopyWhitelistedComponents(Camera sourceCamera, Camera targetCamera, HashSet<string> whitelist)
        {
            int copiedCount = 0;
            if (sourceCamera == null || targetCamera == null || whitelist.Count == 0)
            {
                return copiedCount;
            }

            var sourceComponents = sourceCamera.GetComponents<Component>();
            for (int i = 0; i < sourceComponents.Length; i++)
            {
                Component sourceComponent = sourceComponents[i];
                if (sourceComponent == null)
                {
                    continue;
                }

                string typeName = GetComponentTypeName(sourceComponent);
                if (!IsWhitelisted(typeName, whitelist))
                {
                    continue;
                }

                if (CopyComponent(sourceCamera, sourceComponent, targetCamera, typeName))
                {
                    copiedCount++;
                }
            }

            return copiedCount;
        }

        private static bool HasWhitelistedPostProcessing(Camera sourceCamera, HashSet<string> componentWhitelist, HashSet<string> pp2Whitelist)
        {
            if (sourceCamera == null)
            {
                return false;
            }

            if (componentWhitelist.Count > 0)
            {
                var sourceComponents = sourceCamera.GetComponents<Component>();
                for (int i = 0; i < sourceComponents.Length; i++)
                {
                    Component sourceComponent = sourceComponents[i];
                    if (sourceComponent != null && IsWhitelisted(GetComponentTypeName(sourceComponent), componentWhitelist))
                    {
                        return true;
                    }
                }
            }

            if (pp2Whitelist.Count == 0)
            {
                return false;
            }

            PostProcessLayer sourceLayer = sourceCamera.GetComponent<PostProcessLayer>();
            if (sourceLayer == null || sourceLayer.m_Resources == null)
            {
                return false;
            }

            PostProcessVolume[] sourceVolumes = Object.FindObjectsOfType<PostProcessVolume>();
            for (int i = 0; i < sourceVolumes.Length; i++)
            {
                PostProcessVolume sourceVolume = sourceVolumes[i];
                if (!ShouldCopySourceVolume(sourceLayer, sourceVolume))
                {
                    continue;
                }

                PostProcessProfile profile = sourceVolume.sharedProfile;
                if (profile == null)
                {
                    profile = sourceVolume.profile;
                }

                if (profile == null || profile.settings == null)
                {
                    continue;
                }

                var sourceSettings = profile.settings;
                for (int j = 0; j < sourceSettings.Count; j++)
                {
                    PostProcessEffectSettings sourceSetting = sourceSettings[j];
                    if (sourceSetting != null && IsWhitelisted(GetObjectTypeName(sourceSetting), pp2Whitelist))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool CopyComponent(Camera sourceCamera, Component sourceComponent, Camera targetCamera, string typeName)
        {
            try
            {
                var componentType = sourceComponent.GetIl2CppType();
                Component targetComponent = targetCamera.gameObject.AddComponent(componentType);
                if (targetComponent == null)
                {
                    return false;
                }

                ownedObjects.Add(targetComponent);
                CopySerializedFields(sourceComponent, targetComponent);

                Behaviour sourceBehaviour = sourceComponent.TryCast<Behaviour>();
                Behaviour targetBehaviour = targetComponent.TryCast<Behaviour>();
                if (sourceBehaviour != null && targetBehaviour != null)
                {
                    targetBehaviour.enabled = sourceBehaviour.enabled;
                }

                RetargetCameraFields(targetComponent, sourceCamera.transform, targetCamera.transform);
                ResetCachedMaterials(targetComponent);
                CopyKnownPostProcessLayerSettings(sourceComponent, targetComponent, sourceCamera, targetCamera);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private Pp2CopySummary CopyFilteredPostProcessVolumes(Camera sourceCamera, Camera targetCamera, VrRuntimeSettings settings, HashSet<string> effectWhitelist)
        {
            Pp2CopySummary summary = new Pp2CopySummary();
            if (sourceCamera == null || targetCamera == null || settings == null || effectWhitelist.Count == 0)
            {
                return summary;
            }

            PostProcessLayer sourceLayer = sourceCamera.GetComponent<PostProcessLayer>();
            if (sourceLayer == null || sourceLayer.m_Resources == null)
            {
                return summary;
            }

            bool copiedAnyProfile = false;
            PostProcessVolume[] sourceVolumes = Object.FindObjectsOfType<PostProcessVolume>();
            for (int i = 0; i < sourceVolumes.Length; i++)
            {
                PostProcessVolume sourceVolume = sourceVolumes[i];
                if (!ShouldCopySourceVolume(sourceLayer, sourceVolume))
                {
                    continue;
                }

                PostProcessProfile filteredProfile = CreateFilteredProfile(sourceVolume, effectWhitelist, out int copiedEffects);
                if (filteredProfile == null)
                {
                    continue;
                }

                CreateFilteredVolume(targetCamera, sourceVolume, filteredProfile, settings.VrPp2VolumeLayer);
                summary.Volumes++;
                summary.Effects += copiedEffects;
                copiedAnyProfile = true;
            }

            if (!copiedAnyProfile)
            {
                return summary;
            }

            PostProcessLayer targetLayer = targetCamera.gameObject.AddComponent<PostProcessLayer>();
            if (targetLayer == null)
            {
                return summary;
            }

            ownedObjects.Add(targetLayer);
            targetLayer.volumeTrigger = targetCamera.transform;
            LayerMask volumeLayer = new LayerMask();
            volumeLayer.value = 1 << settings.VrPp2VolumeLayer;
            targetLayer.volumeLayer = volumeLayer;
            targetLayer.stopNaNPropagation = sourceLayer.stopNaNPropagation;
            targetLayer.finalBlitToCameraTarget = sourceLayer.finalBlitToCameraTarget;
            targetLayer.antialiasingMode = PostProcessLayer.Antialiasing.None;
            targetLayer.fog = sourceLayer.fog;
            targetLayer.dithering = sourceLayer.dithering;
            targetLayer.breakBeforeColorGrading = sourceLayer.breakBeforeColorGrading;
            targetLayer.m_Resources = sourceLayer.m_Resources;
            targetLayer.Init(sourceLayer.m_Resources);
            targetLayer.InitBundles();
            return summary;
        }

        private static bool ShouldCopySourceVolume(PostProcessLayer sourceLayer, PostProcessVolume sourceVolume)
        {
            if (sourceLayer == null || sourceVolume == null || !sourceVolume.isActiveAndEnabled || !sourceVolume.isGlobal)
            {
                return false;
            }

            int sourceMask = sourceLayer.volumeLayer.value;
            int sourceVolumeLayer = 1 << sourceVolume.gameObject.layer;
            return (sourceMask & sourceVolumeLayer) != 0;
        }

        private PostProcessProfile CreateFilteredProfile(PostProcessVolume sourceVolume, HashSet<string> effectWhitelist, out int copiedEffects)
        {
            copiedEffects = 0;
            PostProcessProfile sourceProfile = sourceVolume.sharedProfile;
            if (sourceProfile == null)
            {
                sourceProfile = sourceVolume.profile;
            }

            if (sourceProfile == null || sourceProfile.settings == null)
            {
                return null;
            }

            PostProcessProfile targetProfile = ScriptableObject.CreateInstance<PostProcessProfile>();
            if (targetProfile == null)
            {
                return null;
            }

            ownedObjects.Add(targetProfile);
            bool copiedAnySetting = false;
            var sourceSettings = sourceProfile.settings;
            for (int i = 0; i < sourceSettings.Count; i++)
            {
                PostProcessEffectSettings sourceSetting = sourceSettings[i];
                if (sourceSetting == null)
                {
                    continue;
                }

                string typeName = GetObjectTypeName(sourceSetting);
                if (!IsWhitelisted(typeName, effectWhitelist))
                {
                    continue;
                }

                try
                {
                    PostProcessEffectSettings targetSetting = targetProfile.AddSettings(sourceSetting.GetIl2CppType());
                    if (targetSetting == null)
                    {
                        continue;
                    }

                    CopySerializedFields(sourceSetting, targetSetting);
                    copiedEffects++;
                    copiedAnySetting = true;
                }
                catch (Exception)
                {
                }
            }

            return copiedAnySetting ? targetProfile : null;
        }

        private void CreateFilteredVolume(Camera targetCamera, PostProcessVolume sourceVolume, PostProcessProfile filteredProfile, int volumeLayer)
        {
            GameObject volumeObject = new GameObject("SecretFlasherManakaVR_FilteredPPv2Volume");
            ownedObjects.Add(volumeObject);
            volumeObject.hideFlags = HideFlags.HideAndDontSave;
            volumeObject.layer = volumeLayer;
            volumeObject.transform.SetParent(targetCamera.transform, false);

            PostProcessVolume targetVolume = volumeObject.AddComponent<PostProcessVolume>();
            targetVolume.enabled = false;
            targetVolume.sharedProfile = filteredProfile;
            targetVolume.isGlobal = true;
            targetVolume.blendDistance = sourceVolume.blendDistance;
            targetVolume.weight = sourceVolume.weight;
            targetVolume.priority = sourceVolume.priority;
            targetVolume.enabled = sourceVolume.enabled;
        }

        private static void CopySerializedFields(Component sourceComponent, Component targetComponent)
        {
            try
            {
                string json = JsonUtility.ToJson(sourceComponent, false);
                if (!string.IsNullOrEmpty(json))
                {
                    JsonUtility.FromJsonOverwrite(json, targetComponent);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void CopySerializedFields(ScriptableObject sourceObject, ScriptableObject targetObject)
        {
            try
            {
                string json = JsonUtility.ToJson(sourceObject, false);
                if (!string.IsNullOrEmpty(json))
                {
                    JsonUtility.FromJsonOverwrite(json, targetObject);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void CopyKnownPostProcessLayerSettings(Component sourceComponent, Component targetComponent, Camera sourceCamera, Camera targetCamera)
        {
            PostProcessLayer sourceLayer = sourceComponent.TryCast<PostProcessLayer>();
            PostProcessLayer targetLayer = targetComponent.TryCast<PostProcessLayer>();
            if (sourceLayer == null || targetLayer == null)
            {
                return;
            }

            targetLayer.volumeTrigger = sourceLayer.volumeTrigger == null || sourceLayer.volumeTrigger == sourceCamera.transform
                ? targetCamera.transform
                : sourceLayer.volumeTrigger;
            targetLayer.volumeLayer = sourceLayer.volumeLayer;
            targetLayer.stopNaNPropagation = sourceLayer.stopNaNPropagation;
            targetLayer.finalBlitToCameraTarget = sourceLayer.finalBlitToCameraTarget;
            targetLayer.antialiasingMode = sourceLayer.antialiasingMode == PostProcessLayer.Antialiasing.TemporalAntialiasing
                ? PostProcessLayer.Antialiasing.None
                : sourceLayer.antialiasingMode;
            targetLayer.fog = sourceLayer.fog;
            targetLayer.dithering = sourceLayer.dithering;
            targetLayer.breakBeforeColorGrading = sourceLayer.breakBeforeColorGrading;
            targetLayer.m_Resources = sourceLayer.m_Resources;

            if (sourceLayer.m_Resources != null)
            {
                targetLayer.Init(sourceLayer.m_Resources);
                targetLayer.InitBundles();
            }
        }

        private static void RetargetCameraFields(Component component, Transform sourceTransform, Transform targetTransform)
        {
            TrySetTransformField(component, "CamTransform", sourceTransform, targetTransform);
            TrySetTransformField(component, "CameraTransform", sourceTransform, targetTransform);
            TrySetTransformField(component, "cameraTransform", sourceTransform, targetTransform);
        }

        private static void TrySetTransformField(Component component, string fieldName, Transform sourceTransform, Transform targetTransform)
        {
            if (component == null || targetTransform == null)
            {
                return;
            }

            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(component.ObjectClass, fieldName);
            if (field == IntPtr.Zero)
            {
                return;
            }

            IntPtr boxed = IL2CPP.il2cpp_field_get_value_object(field, component.Pointer);
            if (boxed != IntPtr.Zero)
            {
                var existing = new Il2CppSystem.Object(boxed).TryCast<Transform>();
                if (existing != null && sourceTransform != null && existing != sourceTransform)
                {
                    return;
                }
            }

            IL2CPP.il2cpp_field_set_value_object(component.Pointer, field, targetTransform.Pointer);
        }

        private static void ResetCachedMaterials(Component component)
        {
            TrySetObjectField(component, "_material", IntPtr.Zero);
        }

        private static void TrySetObjectField(Component component, string fieldName, IntPtr value)
        {
            if (component == null)
            {
                return;
            }

            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(component.ObjectClass, fieldName);
            if (field != IntPtr.Zero)
            {
                IL2CPP.il2cpp_field_set_value_object(component.Pointer, field, value);
            }
        }

        private static bool IsWhitelisted(string typeName, HashSet<string> whitelist)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            if (whitelist.Contains(typeName))
            {
                return true;
            }

            int lastDot = typeName.LastIndexOf('.');
            return lastDot >= 0 && lastDot < typeName.Length - 1 && whitelist.Contains(typeName.Substring(lastDot + 1));
        }

        private static HashSet<string> BuildWhitelist(string whitelistCsv)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(whitelistCsv))
            {
                return result;
            }

            string[] entries = whitelistCsv.Split(',');
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i].Trim();
                if (!string.IsNullOrEmpty(entry))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        private static string GetComponentTypeName(Component component)
        {
            return GetObjectTypeName(component);
        }

        private static string GetObjectTypeName(Object unityObject)
        {
            if (unityObject == null)
            {
                return string.Empty;
            }

            IntPtr klass = unityObject.ObjectClass;
            string className = IL2CPP.il2cpp_class_get_name_(klass) ?? string.Empty;
            string namespaceName = IL2CPP.il2cpp_class_get_namespace_(klass) ?? string.Empty;
            return string.IsNullOrEmpty(namespaceName) ? className : namespaceName + "." + className;
        }

        private struct Pp2CopySummary
        {
            public int Volumes;
            public int Effects;
        }
    }
}
