using System;
using System.Collections.Generic;
using ExposureUnnoticed2.Object3D.ScenePlops.ConbiniBoxFog;
using ExposureUnnoticed2.Object3D.ScenePlops.WallFog;
using ExposureUnnoticed2.Scripts.InGame;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppMaterialList = Il2CppSystem.Collections.Generic.List<UnityEngine.Material>;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class EyeMaskWeatherFogSuppressor
    {
        private const float ActiveVisibilityThreshold = 0.05f;
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly string[] EmptyPaths = new string[0];
        private static readonly SceneFogPathSet[] SceneFogPaths =
        {
            new SceneFogPathSet(
                "Mall",
                EmptyPaths,
                new[]
                {
                    "Level/Environment/BoxFogs/Floor1/BoxFog",
                    "Level/Environment/BoxFogs/Floor1/BoxFog (1)",
                    "Level/Environment/BoxFogs/Floor1/BoxFog (2)",
                    "Level/Environment/BoxFogs/Floor2/BoxFog (12)",
                    "Level/Environment/BoxFogs/Soto/BoxFog (11)",
                    "Level/Environment/BoxFogs/Soto/BoxFog (12)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntrancePlops/BoxFog (9)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntrancePlops/BoxFog (10)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntranceRoute/BoxFog (12)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntranceRoute/BoxFog (13)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntranceRoute/BoxFog (21)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntranceRoute/BoxFog (22)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntranceRoute/BoxFog (23)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntranceRoute/BoxFog (24)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntranceRoute/BoxFog (25)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntranceRoute/BoxFog (26)",
                    "Level/Environment/ObjectCullerGeometory/ObjectCullerTarget/EntranceRoute/BoxFog (27)",
                }),
            new SceneFogPathSet(
                "Konbini",
                EmptyPaths,
                new[]
                {
                    "Level/ConbiniBoxFog",
                }),
            new SceneFogPathSet(
                "Night_Demo 1",
                new[]
                {
                    "Level/Environment/WallFog",
                    "Level/Environment/WallFog (1)",
                },
                new[]
                {
                    "Level/Environment/Convini/ConbiniBoxFog",
                }),
            new SceneFogPathSet(
                "StationFront",
                new[]
                {
                    "Level/Environment/WallFog",
                    "Level/Environment/WallFog (1)",
                },
                EmptyPaths),
            new SceneFogPathSet(
                "MyPark",
                new[]
                {
                    "Level/Environment/WallFOg/WallFog",
                    "Level/Environment/WallFOg/WallFog (1)",
                    "Level/Environment/WallFOg/WallFog (2)",
                    "Level/Environment/WallFOg/WallFog (3)",
                },
                EmptyPaths),
        };

        private readonly HashSet<int> suppressedMaterialPropertyIds = new HashSet<int>();
        private readonly HashSet<int> suppressedRendererIds = new HashSet<int>();
        private readonly List<Action> restoreActions = new List<Action>();
        private readonly List<WallFogController> wallFogControllers = new List<WallFogController>();
        private readonly List<BoxFogController> boxFogControllers = new List<BoxFogController>();
        private bool suppressing;
        private int cachedSceneHandle = -1;

        public void Tick()
        {
            RefreshSceneCacheIfNeeded();

            if (!IsEyeMaskVisibilityActive())
            {
                Restore();
                return;
            }

            suppressing = true;
            SuppressWallFogs();
            SuppressBoxFogs();
        }

        public void Shutdown()
        {
            Restore();
            wallFogControllers.Clear();
            boxFogControllers.Clear();
            cachedSceneHandle = -1;
        }

        private static bool IsEyeMaskVisibilityActive()
        {
            VisibilityFogController controller = VisibilityFogController.Instance;
            if (controller == null)
            {
                return false;
            }

            if (controller.IsBadVisibility)
            {
                return true;
            }

            return controller.visibilityLevel != null && controller.visibilityLevel.Current > ActiveVisibilityThreshold;
        }

        private void RefreshSceneCacheIfNeeded()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.handle == cachedSceneHandle)
            {
                return;
            }

            Restore();
            wallFogControllers.Clear();
            boxFogControllers.Clear();
            cachedSceneHandle = scene.handle;

            SceneFogPathSet pathSet = FindPathSet(scene.name);
            if (pathSet == null)
            {
                return;
            }

            for (int i = 0; i < pathSet.WallFogPaths.Length; i++)
            {
                WallFogController controller = FindComponentAtPath<WallFogController>(scene, pathSet.WallFogPaths[i]);
                if (controller != null)
                {
                    wallFogControllers.Add(controller);
                }
            }

            for (int i = 0; i < pathSet.BoxFogPaths.Length; i++)
            {
                BoxFogController controller = FindComponentAtPath<BoxFogController>(scene, pathSet.BoxFogPaths[i]);
                if (controller != null)
                {
                    boxFogControllers.Add(controller);
                }
            }
        }

        private static SceneFogPathSet FindPathSet(string sceneName)
        {
            for (int i = 0; i < SceneFogPaths.Length; i++)
            {
                if (SceneFogPaths[i].SceneName == sceneName)
                {
                    return SceneFogPaths[i];
                }
            }

            return null;
        }

        private static T FindComponentAtPath<T>(Scene scene, string path)
            where T : Component
        {
            Transform transform = FindTransformAtPath(scene, path);
            return transform == null ? null : transform.GetComponent<T>();
        }

        private static Transform FindTransformAtPath(Scene scene, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            int slashIndex = path.IndexOf('/');
            string rootName = slashIndex < 0 ? path : path.Substring(0, slashIndex);
            string childPath = slashIndex < 0 ? string.Empty : path.Substring(slashIndex + 1);
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null || root.name != rootName)
                {
                    continue;
                }

                if (childPath.Length == 0)
                {
                    return root.transform;
                }

                Transform child = root.transform.Find(childPath);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private void Restore()
        {
            if (!suppressing && restoreActions.Count == 0)
            {
                return;
            }

            for (int i = restoreActions.Count - 1; i >= 0; i--)
            {
                restoreActions[i]();
            }

            restoreActions.Clear();
            suppressedMaterialPropertyIds.Clear();
            suppressedRendererIds.Clear();
            suppressing = false;
        }

        private void SuppressWallFogs()
        {
            for (int i = 0; i < wallFogControllers.Count; i++)
            {
                WallFogController controller = wallFogControllers[i];
                if (controller == null)
                {
                    continue;
                }

                SuppressMaterials(controller.childMaterialList, WallFogController.Color1);
                SuppressRenderers(controller.GetComponentsInChildren<Renderer>(true));
            }
        }

        private void SuppressBoxFogs()
        {
            for (int i = 0; i < boxFogControllers.Count; i++)
            {
                BoxFogController controller = boxFogControllers[i];
                if (controller == null)
                {
                    continue;
                }

                SuppressMaterial(controller.material, ColorPropertyId);
                SuppressMaterial(controller.material, BaseColorPropertyId);
                SuppressRenderers(controller.GetComponentsInChildren<Renderer>(true));
            }
        }

        private void SuppressMaterials(Il2CppMaterialList materials, int propertyId)
        {
            if (materials == null)
            {
                return;
            }

            for (int i = 0; i < materials.Count; i++)
            {
                SuppressMaterial(materials[i], propertyId);
            }
        }

        private void SuppressMaterial(Material material, int propertyId)
        {
            if (material == null || propertyId == 0 || !material.HasProperty(propertyId))
            {
                return;
            }

            int id = material.GetInstanceID() ^ propertyId;
            if (suppressedMaterialPropertyIds.Add(id))
            {
                Color original = material.GetColor(propertyId);
                restoreActions.Add(() =>
                {
                    if (material != null)
                    {
                        material.SetColor(propertyId, original);
                    }
                });
            }

            Color color = material.GetColor(propertyId);
            color.a = 0.0f;
            material.SetColor(propertyId, color);
        }

        private void SuppressRenderers(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                int id = renderer.GetInstanceID();
                if (suppressedRendererIds.Add(id))
                {
                    bool originalEnabled = renderer.enabled;
                    restoreActions.Add(() =>
                    {
                        if (renderer != null)
                        {
                            renderer.enabled = originalEnabled;
                        }
                    });
                }

                renderer.enabled = false;
            }
        }

        private sealed class SceneFogPathSet
        {
            public readonly string SceneName;
            public readonly string[] WallFogPaths;
            public readonly string[] BoxFogPaths;

            public SceneFogPathSet(string sceneName, string[] wallFogPaths, string[] boxFogPaths)
            {
                SceneName = sceneName;
                WallFogPaths = wallFogPaths;
                BoxFogPaths = boxFogPaths;
            }
        }
    }
}
