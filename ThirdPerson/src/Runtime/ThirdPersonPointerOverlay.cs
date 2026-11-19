using System;
using System.Numerics;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Player;
using UnityEngine;
using PreLuna.ThirdPerson.Core;
using NV=System.Numerics.Vector3;
using NV2=System.Numerics.Vector2;
using UV2=UnityEngine.Vector2;

namespace PreLuna.ThirdPerson.Runtime
{
    public sealed partial class ThirdPersonRuntime
    {
        [HideFromIl2Cpp]
        void DrawPointerOverlay()
        {
            var settings=Plugin.Instance?.Settings;
            if(settings==null)return;
            // The phase-0 probe has its own centre/target markers; keep one
            // diagnostic language on screen while it is enabled.
            if(settings.PointerOverlay.Value)return;
            if(lookInput.Orientation.Active||!OwnObservationView())return;
            if(settings.PointerDot.Value&&(!aim.Active||shotAim.Valid))DrawPointerDot();
            if(settings.PointerTargetMarker.Value)DrawPointerTargetMarker();
        }

        [HideFromIl2Cpp]
        void DrawPointerDot()
        {
            var center=PointerCenter();
            if(Plugin.Instance.Settings.PointerMeleeEyeBasis.Value
                &&IsMeleeWeapon(player?.Inventory?.WieldedItem)
                &&TryMeleeEyeScreen(out var meleeScreen))center=meleeScreen;
            DrawPointerDotAt(center);
        }

        [HideFromIl2Cpp]
        static void DrawPointerDotAt(NV2 center)
        {
            float scale=PointerOverlayScale();
            float size=Mathf.Max(3f,3f*scale);
            float outline=Mathf.Max(1f,scale);
            var old=GUI.color;
            try
            {
                GUI.color=new Color(0,0,0,.8f);
                GUI.DrawTexture(new Rect(center.X-size*.5f-outline,center.Y-size*.5f-outline,size+outline*2,size+outline*2),Texture2D.whiteTexture);
                GUI.color=Color.white;
                GUI.DrawTexture(new Rect(center.X-size*.5f,center.Y-size*.5f,size,size),Texture2D.whiteTexture);
            }
            finally {GUI.color=old;}
        }

        [HideFromIl2Cpp]
        static bool IsMeleeWeapon(ItemEquippable item)
        {
            try{return item!=null&&item.pItemData.slot==InventorySlot.GearMelee;}
            catch{return false;}
        }

        [HideFromIl2Cpp]
        bool TryMeleeEyeScreen(out NV2 screen)
        {
            screen=default;
            var fps=player?.FPSCamera;
            if(fps==null||!pointerCameraValid)return false;
            BindPointerWorld();
            var origin=ConvertView.N(fps.Position);
            var direction=ConvertView.N(fps.Forward);
            if(!Finite(origin)||!Finite(direction)||direction.LengthSquared()<.000001f)return false;
            direction=NV.Normalize(direction);
            const float reach=2.5f;
            NV point;
            if(pointerWorld.RayHit(origin,direction,reach,out var hit))
                point=hit.collider!=null?ConvertView.N(hit.point):origin;
            else point=origin+direction*reach;
            return PointerProjection.TryProject(pointerCamera,point,Screen.height,out screen);
        }

        [HideFromIl2Cpp]
        void DrawPointerTargetMarker()
        {
            if(player==null)return;
            var controller=player.Interaction;
            var target=controller?.m_bestSelectedInteract?.TryCast<Interact_Base>();
            if(controller==null||target==null||!target.IsSelected||!target.IsActive)return;
            if(Id(controller.m_owner)!=Id(player)||!PlayerInteraction.InteractionEnabled)return;
            if(!pointerCameraValid)return;
            NV point;
            try
            {
                var root=target.Root;
                if(root!=null)point=ConvertView.N(root.position);
                else if(target.m_colliderToOwn!=null)point=ConvertView.N(target.m_colliderToOwn.bounds.center);
                else point=ConvertView.N(target.transform.position);
            }
            catch {return;}
            if(!PointerProjection.TryProject(pointerCamera,point,Screen.height,true,out var screen))return;
            float margin=Mathf.Max(12f,12f*PointerOverlayScale());
            var pixel=new UV2(Mathf.Clamp(screen.X,margin,Mathf.Max(margin,Screen.width-margin)),
                Mathf.Clamp(screen.Y,margin,Mathf.Max(margin,Screen.height-margin)));
            DrawTargetBracket(pixel,target.IsBlocked);
        }

        [HideFromIl2Cpp]
        static void DrawTargetBracket(UV2 point,bool blocked)
        {
            float scale=PointerOverlayScale();
            float half=7f*scale;
            float length=3f*scale;
            float thickness=Mathf.Max(1f,scale);
            var color=blocked?new Color(.65f,.85f,1f,.45f):new Color(.25f,.9f,1f,.95f);
            var old=GUI.color;
            try
            {
                GUI.color=color;
                // Four corner brackets, each with a short horizontal and vertical tick.
                Tick(point.x-half,point.y-half,length,thickness);
                Tick(point.x-half,point.y-half,thickness,length);
                Tick(point.x+half-length,point.y-half,length,thickness);
                Tick(point.x+half-thickness,point.y-half,thickness,length);
                Tick(point.x-half,point.y+half-thickness,length,thickness);
                Tick(point.x-half,point.y+half-length,thickness,length);
                Tick(point.x+half-length,point.y+half-thickness,length,thickness);
                Tick(point.x+half-thickness,point.y+half-length,thickness,length);
            }
            finally {GUI.color=old;}
        }

        [HideFromIl2Cpp]
        static void Tick(float x,float y,float width,float height)
        {
            GUI.DrawTexture(new Rect(x-1,y-1,width+2,height+2),Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x,y,width,height),Texture2D.whiteTexture);
        }

        [HideFromIl2Cpp]
        static float PointerOverlayScale()=>Mathf.Clamp(Screen.height/1080f,.75f,2.5f);
    }
}
