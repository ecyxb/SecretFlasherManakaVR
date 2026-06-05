using System;
using System.Collections.Generic;
using AkilliMum.Standard.Mirror;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class VrMirrorRenderer
    {
        private readonly IVrRuntimeLogger logger;
        private readonly List<MirrorEntry> entries = new List<MirrorEntry>();
        private readonly HashSet<int> skippedReflectiveObjectWarnings = new HashSet<int>();
        private const int MaxConsecutiveRenderFailures = 3;
        private int nextEntryIndex;
        private bool needsSceneMirrorScan = true;

        public VrMirrorRenderer(IVrRuntimeLogger logger)
        {
            this.logger = logger ?? NullVrRuntimeLogger.Instance;
        }

        public void Tick(VrRuntimeSettings settings, Camera eyeCamera, Vector3 headPosition, RuntimeEye eye)
        {
            if (settings == null || !settings.EnableVrMirrorRenderer || eyeCamera == null)
            {
                return;
            }

            if (needsSceneMirrorScan)
            {
                ScanMirrors();
                needsSceneMirrorScan = false;
            }

            RemoveInvalidEntries();
            if (entries.Count == 0)
            {
                return;
            }

            int updatesThisFrame = 0;
            int maxUpdates = Mathf.Clamp(settings.VrMirrorMaxUpdatesPerFrame, 1, 4);
            int checkedCount = 0;
            while (checkedCount < entries.Count && updatesThisFrame < maxUpdates)
            {
                if (nextEntryIndex >= entries.Count)
                {
                    nextEntryIndex = 0;
                }

                MirrorEntry entry = entries[nextEntryIndex++];
                checkedCount++;
                if (!ShouldUpdate(entry, settings, headPosition, eye))
                {
                    continue;
                }

                RenderMirror(entry, eyeCamera, eye);
                updatesThisFrame++;
            }
        }

        public void OnSceneChanged()
        {
            entries.Clear();
            skippedReflectiveObjectWarnings.Clear();
            nextEntryIndex = 0;
            needsSceneMirrorScan = true;
        }

        public void Shutdown()
        {
            OnSceneChanged();
        }

        private void ScanMirrors()
        {
            MirrorManager[] managers = UnityEngine.Object.FindObjectsOfType<MirrorManager>();
            for (int i = 0; i < managers.Length; i++)
            {
                MirrorManager manager = managers[i];
                if (!IsUsableMirrorManager(manager) || FindEntry(manager) != null)
                {
                    continue;
                }

                MirrorEntry entry = CreateEntry(manager);
                if (entry == null)
                {
                    continue;
                }

                entries.Add(entry);
                logger.Info(
                    "VR mirror renderer attached to original MirrorManager " +
                    ReflectionBlocker.GetPath(manager.gameObject) +
                    ". The original render path will run from VRMod's controlled mirror pass.");
            }
        }

        private MirrorEntry CreateEntry(MirrorManager manager)
        {
            List<Renderer> renderers = CollectReflectiveRenderers(manager);
            if (renderers.Count == 0)
            {
                LogSkippedEmptyReflectiveObjects(manager);
                return null;
            }

            return new MirrorEntry(manager, renderers[0]);
        }

        private List<Renderer> CollectReflectiveRenderers(MirrorManager manager)
        {
            List<Renderer> renderers = new List<Renderer>();
            try
            {
                Il2CppReferenceArray<GameObject> reflectiveObjects = manager.ReflectiveObjects;
                if (reflectiveObjects != null)
                {
                    for (int i = 0; i < reflectiveObjects.Length; i++)
                    {
                        AddRenderers(reflectiveObjects[i], renderers);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warning("Failed to read MirrorManager.ReflectiveObjects: " + ex.Message);
            }

            return renderers;
        }

        private static void AddRenderers(GameObject rootObject, List<Renderer> renderers)
        {
            if (rootObject == null)
            {
                return;
            }

            Renderer[] found = rootObject.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < found.Length; i++)
            {
                Renderer renderer = found[i];
                if (renderer != null && !renderers.Contains(renderer))
                {
                    renderers.Add(renderer);
                }
            }
        }

        private void RenderMirror(MirrorEntry entry, Camera eyeCamera, RuntimeEye eye)
        {
            VrRuntimeState.BeginVrMirrorRender(null);
            try
            {
                entry.Manager.RenderReflective(eyeCamera, true, true);
                entry.MarkRenderSuccess();
                entry.SetLastRenderFrame(eye, Time.frameCount);
            }
            catch (Exception ex)
            {
                if (entry.MarkRenderFailure(MaxConsecutiveRenderFailures))
                {
                    logger.Warning(
                        "Disabling VR mirror renderer for " +
                        ReflectionBlocker.GetPath(entry.Manager.gameObject) +
                        " after " +
                        entry.ConsecutiveRenderFailures +
                        " consecutive original RenderReflective failures: " +
                        CompactException(ex));
                }
            }
            finally
            {
                VrRuntimeState.EndVrMirrorRender();
            }
        }

        private void LogSkippedEmptyReflectiveObjects(MirrorManager manager)
        {
            int id = manager.GetInstanceID();
            if (!skippedReflectiveObjectWarnings.Add(id))
            {
                return;
            }

            logger.Info(
                "Skipping VR mirror renderer for " +
                ReflectionBlocker.GetPath(manager.gameObject) +
                " because MirrorManager.ReflectiveObjects is empty or has no renderers.");
        }

        private bool ShouldUpdate(MirrorEntry entry, VrRuntimeSettings settings, Vector3 headPosition, RuntimeEye eye)
        {
            if (!entry.IsValid || entry.RenderDisabled)
            {
                return false;
            }

            if (settings.VrMirrorMaxDistance > 0.0f &&
                entry.FirstRenderer != null &&
                Vector3.Distance(headPosition, entry.FirstRenderer.bounds.center) > settings.VrMirrorMaxDistance)
            {
                return false;
            }

            int interval = Mathf.Max(1, settings.VrMirrorUpdateIntervalFrames);
            int lastRenderFrame = entry.GetLastRenderFrame(eye);
            return lastRenderFrame < 0 || Time.frameCount - lastRenderFrame >= interval;
        }

        private void RemoveInvalidEntries()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].IsValid)
                {
                    continue;
                }

                entries.RemoveAt(i);
            }
        }

        private MirrorEntry FindEntry(MirrorManager manager)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Manager == manager)
                {
                    return entries[i];
                }
            }

            return null;
        }

        private static string CompactException(Exception ex)
        {
            if (ex == null)
            {
                return "unknown exception";
            }

            string message = ex.Message ?? string.Empty;
            int newline = message.IndexOfAny(new[] { '\r', '\n' });
            if (newline >= 0)
            {
                message = message.Substring(0, newline);
            }

            if (message.Length == 0)
            {
                return ex.GetType().Name;
            }

            return ex.GetType().Name + ": " + message;
        }

        private static bool IsUsableMirrorManager(MirrorManager manager)
        {
            try
            {
                return manager != null &&
                    manager.gameObject != null &&
                    manager.gameObject.activeInHierarchy;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private sealed class MirrorEntry
        {
            public MirrorEntry(MirrorManager manager, Renderer firstRenderer)
            {
                Manager = manager;
                FirstRenderer = firstRenderer;
                LastLeftRenderFrame = -1;
                LastRightRenderFrame = -1;
            }

            public MirrorManager Manager { get; }

            public Renderer FirstRenderer { get; }

            public int LastLeftRenderFrame { get; private set; }

            public int LastRightRenderFrame { get; private set; }

            public int ConsecutiveRenderFailures { get; private set; }

            public bool RenderDisabled { get; private set; }

            public int GetLastRenderFrame(RuntimeEye eye)
            {
                return eye == RuntimeEye.Left ? LastLeftRenderFrame : LastRightRenderFrame;
            }

            public void SetLastRenderFrame(RuntimeEye eye, int frame)
            {
                if (eye == RuntimeEye.Left)
                {
                    LastLeftRenderFrame = frame;
                }
                else
                {
                    LastRightRenderFrame = frame;
                }
            }

            public void MarkRenderSuccess()
            {
                ConsecutiveRenderFailures = 0;
            }

            public bool MarkRenderFailure(int maxConsecutiveFailures)
            {
                if (RenderDisabled)
                {
                    return false;
                }

                ConsecutiveRenderFailures++;
                if (ConsecutiveRenderFailures < maxConsecutiveFailures)
                {
                    return false;
                }

                RenderDisabled = true;
                return true;
            }

            public bool IsValid
            {
                get
                {
                    return Manager != null &&
                        Manager.gameObject != null &&
                        Manager.gameObject.activeInHierarchy;
                }
            }
        }
    }
}
