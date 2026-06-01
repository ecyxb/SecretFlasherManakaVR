using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecretFlasherManakaVR.Runtime
{
    internal sealed class PlayerSkinningPreRenderRefresher
    {
        private const int CacheRefreshFrameInterval = 120;
        private const int MissingPlayerRetryFrameInterval = 30;
        private readonly List<SkinnedMeshRenderer> playerSkinnedRenderers = new List<SkinnedMeshRenderer>();
        private Animator? playerAnimator;
        private Transform? playerRoot;
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
            playerRoot = null;
            nextCacheRefreshFrame = 0;
            lastRefreshFrame = -1;
        }

        private void EnsureCached(int frame)
        {
            Transform? currentPlayerRoot = SecretFlasherManakaVR.PlayerHeadPoseController.CachedPlayerRoot;
            if (frame < nextCacheRefreshFrame &&
                playerRoot == currentPlayerRoot &&
                playerAnimator != null &&
                playerSkinnedRenderers.Count > 0)
            {
                return;
            }

            playerRoot = currentPlayerRoot;
            if (playerRoot == null)
            {
                playerSkinnedRenderers.Clear();
                playerAnimator = null;
                nextCacheRefreshFrame = frame + MissingPlayerRetryFrameInterval;
                return;
            }

            nextCacheRefreshFrame = frame + CacheRefreshFrameInterval;
            playerAnimator = FindPlayerAnimator(playerRoot);
            CachePlayerSkinnedRenderers(playerRoot);
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

        private void CachePlayerSkinnedRenderers(Transform root)
        {
            playerSkinnedRenderers.Clear();
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                playerSkinnedRenderers.Add(renderer);
            }
        }

        private Animator? FindPlayerAnimator(Transform root)
        {
            Animator[] animators = root.GetComponentsInChildren<Animator>();
            Animator? first = null;
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (animator == null)
                {
                    continue;
                }

                if (first == null)
                {
                    first = animator;
                }

                if (animator.isActiveAndEnabled)
                {
                    return animator;
                }
            }

            return first;
        }
    }
}
