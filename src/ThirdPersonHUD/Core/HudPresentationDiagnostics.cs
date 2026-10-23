using System;

namespace PreLuna.ThirdPersonHUD.Core
{
    public readonly record struct HudPresentationDiagnostics(Guid Instance, long ActualDraws, long TextureTargetRejections,
        long AnchorsCreated, long TexturesCreated, long DestroyRequests, int LiveOwnedReferences, HudPlacement LastDraw);
}
