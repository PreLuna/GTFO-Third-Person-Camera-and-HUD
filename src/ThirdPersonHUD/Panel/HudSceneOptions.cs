using System;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public readonly record struct HudSceneOptions
    {
        public bool Enabled { get; init; }
        public bool Interaction { get; init; }
        public bool Supply { get; init; }
        public bool Mission { get; init; }
        public bool Scan { get; init; }
        public bool Markers { get; init; }
        public bool Rescue { get; init; }
        public bool Pings { get; init; }
        public bool Detected { get; init; }
        public bool ReplaceCountdown { get; init; }
        public int MaximumMarkers { get; init; }
        public int CandidateLimit { get; init; }
        public float MaximumDistance { get; init; }
        public float CardScale { get; init; }
        public float CardWidth { get; init; }
        public float InteractionOffsetX { get; init; }
        public float InteractionBottom { get; init; }
        public float ScreenMargin { get; init; }
        public float MarkerCrosshairRadius { get; init; }
        public static HudSceneOptions Default => new(){Enabled=true,Interaction=true,Supply=true,Mission=true,Scan=true,Markers=true,
            Rescue=true,Pings=true,Detected=true,ReplaceCountdown=true,MaximumMarkers=3,CandidateLimit=16,MaximumDistance=80,CardScale=1,CardWidth=360,InteractionBottom=90,ScreenMargin=20,MarkerCrosshairRadius=72};
        public HudSceneOptions Sanitized()=>this with{MaximumMarkers=Math.Clamp(MaximumMarkers,0,3),CandidateLimit=Math.Clamp(CandidateLimit,1,32),
            MaximumDistance=HudPanelLayoutOptions.Clamp(MaximumDistance,5,500,80),CardScale=HudPanelLayoutOptions.Clamp(CardScale,.65f,1.4f,1),
            CardWidth=HudPanelLayoutOptions.Clamp(CardWidth,280,480,360),InteractionOffsetX=HudPanelLayoutOptions.Clamp(InteractionOffsetX,-600,600,0),
            InteractionBottom=HudPanelLayoutOptions.Clamp(InteractionBottom,20,400,90),ScreenMargin=HudPanelLayoutOptions.Clamp(ScreenMargin,8,100,20),MarkerCrosshairRadius=HudPanelLayoutOptions.Clamp(MarkerCrosshairRadius,0,200,72)};
        public HudSceneCollectionOptions Collection => new(Enabled,Interaction,Supply,Mission,Scan,Markers,Rescue,Pings,Detected,CandidateLimit);
    }
}
