using UnityEngine;

namespace PreLuna.ThirdPersonHUD.Runtime
{
    // 0.15.1 uses CanvasGroup alpha instead of toggling the native prompt
    // GameObject every frame. Keeping the small operation here gives the test
    // recipe a narrow seam without pulling in the HUD runtime.
    internal static class HudNativePromptVisibility
    {
        internal static CanvasGroup Ensure(GameObject owner)
        {
            if(owner==null)return null;
            var group=owner.GetComponent<CanvasGroup>();
            if(group==null)group=owner.AddComponent<CanvasGroup>();
            return group;
        }

        internal static void Hide(CanvasGroup group)
        {
            if(group==null)return;
            group.alpha=0f;
            group.interactable=false;
            group.blocksRaycasts=false;
        }

        internal static void Restore(CanvasGroup group,float alpha,bool interactable,bool blocksRaycasts)
        {
            if(group==null)return;
            group.alpha=alpha;
            group.interactable=interactable;
            group.blocksRaycasts=blocksRaycasts;
        }
    }
}
