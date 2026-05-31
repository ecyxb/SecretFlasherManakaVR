using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class PlayerSkinningPreRenderRefresher
    {
        private const int CacheRefreshFrameInterval = 120;
        private readonly List<SkinnedMeshRenderer> playerSkinnedRenderers = new List<SkinnedMeshRenderer>();
        private Animator playerAnimator;
        private int nextCacheRefreshFrame;
        private int lastRefreshFrame = -1;

        public void RefreshBeforeManualRender()
        {
            int frame = Time.frameCount;
            if (lastRefreshFrame == frame)
            {
                return;
            }

            lastRefreshFrame = frame;
            EnsureCached(frame);
            EnableForceMatrixRecalculation();

            if (playerAnimator != null && playerAnimator.isActiveAndEnabled)
            {
                playerAnimator.Update(0.0f);
            }
        }

        public void Reset()
        {
            playerSkinnedRenderers.Clear();
            playerAnimator = null;
            nextCacheRefreshFrame = 0;
            lastRefreshFrame = -1;
        }

        private void EnsureCached(int frame)
        {
            if (frame < nextCacheRefreshFrame && playerAnimator != null && playerSkinnedRenderers.Count > 0)
            {
                return;
            }

            nextCacheRefreshFrame = frame + CacheRefreshFrameInterval;
            playerAnimator = FindPlayerAnimator();
            CachePlayerSkinnedRenderers();
        }

        private void EnableForceMatrixRecalculation()
        {
            for (int i = playerSkinnedRenderers.Count - 1; i >= 0; i--)
            {
                SkinnedMeshRenderer renderer = playerSkinnedRenderers[i];
                if (renderer == null)
                {
                    playerSkinnedRenderers.RemoveAt(i);
                    continue;
                }

                try
                {
                    if (!renderer.forceMatrixRecalculationPerRender)
                    {
                        renderer.forceMatrixRecalculationPerRender = true;
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private void CachePlayerSkinnedRenderers()
        {
            playerSkinnedRenderers.Clear();
            SkinnedMeshRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null || !IsPlayerRenderer(renderer))
                {
                    continue;
                }

                playerSkinnedRenderers.Add(renderer);
            }
        }

        private Animator FindPlayerAnimator()
        {
            Animator[] animators = UnityEngine.Object.FindObjectsOfType<Animator>();
            Animator best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (animator == null)
                {
                    continue;
                }

                string path = GetPath(animator.transform);
                int score = ScorePlayerPath(path, animator.name);
                if (score > bestScore)
                {
                    best = animator;
                    bestScore = score;
                }
            }

            return bestScore > 0 ? best : null;
        }

        private static bool IsPlayerRenderer(SkinnedMeshRenderer renderer)
        {
            Transform rootBone = renderer.rootBone;
            string rootPath = rootBone == null ? string.Empty : GetPath(rootBone);
            string rendererPath = GetPath(renderer.transform);
            return ScorePlayerPath(rootPath, renderer.name) > 0 || ScorePlayerPath(rendererPath, renderer.name) > 0;
        }

        private static int ScorePlayerPath(string path, string objectName)
        {
            int score = 0;
            if (path.IndexOf("InGameManager/Player(Clone)", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 100;
            }

            if (path.IndexOf("/Manustia/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.EndsWith("/Manustia", StringComparison.OrdinalIgnoreCase))
            {
                score += 40;
            }

            if (path.IndexOf("/CosplayParent/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 20;
            }

            if (objectName.IndexOf("shoe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("stocking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("Selestia", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 10;
            }

            return score;
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }
    }
}
