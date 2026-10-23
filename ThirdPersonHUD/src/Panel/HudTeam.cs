using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    public readonly record struct HudTeamRow(int Slot, string Name, string Kind, string Status, string Health,
        string Primary, string Secondary, string Tool, string Infection, string Cue, int Priority, bool Covered);
    public readonly record struct HudTeamPresentation(bool Active, bool Details, HudTeamRow First, HudTeamRow Second, HudTeamRow Third)
    {
        public HudTeamRow this[int index] => index switch { 0 => First, 1 => Second, 2 => Third, _ => default };
        public HudOriginalGroup Coverage => (First.Covered ? Group(First.Slot) : 0) | (Second.Covered ? Group(Second.Slot) : 0) | (Third.Covered ? Group(Third.Slot) : 0);
        public static HudOriginalGroup Group(int slot) => slot >= 0 && slot < 4 ? (HudOriginalGroup)(32 << slot) : 0;
        public HudPrompt Prompt
        {
            get { HudTeamRow best = default; for (int i = 0; i < 3; i++) if (this[i].Priority > best.Priority) best = this[i];
                return best.Priority > 0 ? new(HudPromptKind.Team, "队员" + (best.Slot + 1) + " · " + best.Cue, best.Priority) : default; }
        }
    }
    public static class HudTeamMapper
    {
        readonly struct Reader
        {
            readonly HudRuntimeContext current; readonly HudContentSnapshot snapshot; readonly HudContentPolicy policy; readonly long epoch, revision, member;
            public Reader(HudRuntimeContext c, HudContentSnapshot s, long e, long r, HudContentPolicy p, long m)
            { current = c; snapshot = s; epoch = e; revision = r; policy = p; member = m; }
            public bool Read<T>(HudValue<T> field, out T value, out HudValueState state) where T : struct, IHudContentValue
                => HudTeamRules.TryRead(current, snapshot.Header, field, member, epoch, revision, policy, out value, out state);
            public string Ratio(HudValue<HudQuantity> field, bool infection, out double? amount)
            {
                amount = null;
                if (!Read(field, out var q, out var state)) return HudReadoutFormatting.State(state);
                if (q.Unit != HudUnit.Fraction || infection && q.Amount > 1) return "异常";
                amount = q.Amount; return HudReadoutFormatting.Percent(q.Amount, 0);
            }
        }
        public static HudTeamPresentation Map(in HudRuntimeContext current, in HudContentSnapshot snapshot, long epoch,
            long revision, in HudContentPolicy policy, in HudTeamOptions raw, bool expanded)
        {
            var o = raw.Sanitized();
            if (!o.Enabled) return default;
            var c = current; var s = snapshot; var p = policy;
            int local = c.ExpectedBinding.LocalPlayer.Slot;
            if (local < 0 || local > 3) return default;
            bool roster = HudContentRules.TryRead(c, s.Header, s.Team.Roster, c.ExpectedBinding.LocalPlayer.Agent, HudFreshness.Live,
                epoch, revision, p, out var list, out _) && list.LocalSlot == local;
            HudTeamRow Row(int slot)
            {
                var row = new HudTeamRow(slot, "队员" + (slot + 1), "", "未读取", "—", "未读取", "未读取", "未读取", "未读取", "", 0, false);
                if (!roster) return row;
                var peer = s.Team.GetSlot(slot);
                if (peer.Slot != slot) return row with { Status = "异常" };
                bool occupied = (list.OccupiedMask & (1 << slot)) != 0;
                if (!occupied) return row with { Name = "空位", Status = "", Kind = "" };
                var r = new Reader(c, s, epoch, revision, p, peer.Identity.Stamp.SubjectRevision);
                if (!r.Read(peer.Identity, out var id, out var state) || id.Slot != slot || id.Revision != peer.Identity.Stamp.SubjectRevision)
                    return row with { Status = state == HudValueState.Available ? "异常" : HudReadoutFormatting.State(state) };
                bool name = r.Read(peer.Name, out var text, out _);
                row = row with { Name = name ? CleanName(text.Text) : row.Name, Kind = id.IsBot ? "人机" : "真人" };
                if (!r.Read(peer.Connection, out var connection, out state)) return row with { Status = HudReadoutFormatting.State(state) };
                if (connection.Kind != HudPeerState.Connected || !id.Spawned)
                    return row with { Status = connection.Kind == HudPeerState.Offline ? "离线" : "同步中" };
                string health = r.Ratio(peer.Health, false, out var hp);
                string infection = r.Ratio(peer.Infection, true, out var inf);
                string primary = r.Ratio(peer.PrimaryRatio, false, out var ammo1), secondary = r.Ratio(peer.SecondaryRatio, false, out var ammo2);
                string tool = r.Ratio(peer.ToolRatio, false, out var resource);
                bool life = r.Read(peer.Life, out var lifeValue, out var lifeState);
                string status = life ? lifeValue.Kind switch { HudLifeKind.Downed => "倒地", HudLifeKind.Dead => "死亡", _ => "" } : HudReadoutFormatting.State(lifeState);
                string cue = ""; int priority = 0;
                if (o.Cooperation)
                {
                    if (life && lifeValue.Kind == HudLifeKind.Downed) { cue = "需要救援"; priority = 92; }
                    else if (life && lifeValue.Kind == HudLifeKind.Alive)
                    {
                        if (ammo1.HasValue && ammo1 <= o.LowAmmoRatio || ammo2.HasValue && ammo2 <= o.LowAmmoRatio) { cue = "弹药不足"; priority = 45; }
                        else if (resource.HasValue && resource <= o.LowToolRatio) { cue = "工具不足"; priority = 45; }
                        else if (r.Read(peer.Marked, out var mark, out _) && mark.OccurredAtSeconds <= c.NowSeconds
                            && c.NowSeconds - mark.OccurredAtSeconds < o.MarkSeconds) { cue = "已标记"; priority = 35; }
                    }
                }
                // Only the expanded, fully covered text summary can replace a matching original summary.
                bool covered = expanded && o.Details && name && life && lifeValue.Kind == HudLifeKind.Alive
                    && hp.HasValue && inf.HasValue && ammo1.HasValue && ammo2.HasValue && resource.HasValue;
                return row with { Health = hp.HasValue ? health : "—", Status = hp.HasValue ? status : health,
                    Primary = primary, Secondary = secondary, Tool = tool, Infection = infection, Cue = cue, Priority = priority, Covered = covered };
            }
            int first = local == 0 ? 1 : 0, second = first + 1 == local ? first + 2 : first + 1, third = second + 1 == local ? second + 2 : second + 1;
            return new(true, expanded && o.Details, Row(first), Row(second), Row(third));
        }
        // Player supplied labels cannot insert line breaks, control codes or a second visual row.
        static string CleanName(string value)
        {
            value = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return value.Length > 64 ? value.Substring(0, 64) : value;
        }
    }
    public static class HudTeamDrawing
    {
        public const float CompactHeight = 146, ExpandedHeight = 242;
        public static void Build(in HudPanelPlacement panel, in HudPanelOptions options, in HudTeamPresentation team, HudDrawList output)
        {
            if (!team.Active || !options.Layout.Team) return;
            var o = options.Sanitized(); float s = panel.Scale, w = panel.Bounds.Width / s;
            float top = panel.Expanded ? HudPanelProjection.ExpandedHeight : HudPanelProjection.CompactHeight;
            var text = o.Theme.Text.Brightness(o.Theme.Brightness).Opacity(o.Theme.TextOpacity);
            var dim = o.Theme.Base.Brightness(o.Theme.Brightness).Opacity(o.Theme.TextOpacity * .76f);
            void Text(float x, float y, float width, string value, string region, bool number = false, bool urgent = false)
            { float size = number ? HudPersonalDrawing.FitSize(value, width, 22, o.Typography.BodySize) : o.Typography.LabelSize;
                output.Add(new(HudDrawKind.Text, new(x*s,y*s,width*s,22*s), urgent ? o.Theme.Danger.Opacity(o.Theme.TextOpacity) : number ? text : dim,
                    number ? value : HudPanelDrawing.Ellipsize(value,width,size), size*s,region) { Numeric = number }); }
            output.Add(new(HudDrawKind.Rectangle,new(14*s,top*s,(w-28)*s,s),dim.Opacity(.5f),"",0,"team-separator"));
            Text(14,top+3,w-28,"队伍 · 同步库存", "team-header");
            float step = team.Details ? 70 : 38;
            for (int i = 0; i < 3; i++)
            {
                var r = team[i]; float y = top + 26 + step*i; string region = "team-" + r.Slot;
                Text(14,y,22,(r.Slot+1).ToString(),region+"-slot",true);
                Text(40,y,w-159,r.Name,region+"-name"); Text(w-116,y,40,r.Kind,region+"-kind"); Text(w-76,y,62,r.Health,region+"-health",true);
                string status=string.IsNullOrEmpty(r.Cue)?r.Status:string.IsNullOrEmpty(r.Status)?r.Cue:r.Status+" · "+r.Cue;
                if(!string.IsNullOrEmpty(status))Text(40,y+18,w-54,status,region+"-status",urgent:r.Priority>=90);
                if (!team.Details) continue;
                Text(40,y+36,(w-54)/2,"主武器 "+r.Primary,region+"-primary"); Text(40+(w-54)/2,y+36,(w-54)/2,"副武器 "+r.Secondary,region+"-secondary");
                Text(40,y+52,(w-54)/2,"工具 "+r.Tool,region+"-tool"); Text(40+(w-54)/2,y+52,(w-54)/2,"感染 "+r.Infection,region+"-infection");
            }
        }
    }
}
