using System;
using PreLuna.ThirdPerson.Contracts;
using PreLuna.ThirdPersonHUD.Content;

namespace PreLuna.ThirdPersonHUD.Panel
{
    // Drawing for the two archived wireframe layouts:
    //   FourCorner -> screen-edge mission/team/personal/equipment panels.
    //   Radial     -> character-anchored health/infection arcs, mission/squad tags and loadout strip.
    // Both use the same validated view model and never invent a distance, pickup count or carry percentage.
    public static class HudClassicDrawing
    {
        public static void Build(in HudPanelPlacement panel, in HudPanelOptions raw, in HudPanelVisual visual, in HudPanelView view, HudDrawList output)
        {
            output.Clear();
            if (!panel.IsValid) return;
            var options = raw.Sanitized();
            var canvas = new Canvas(panel, options, visual, output);
            if (options.Layout.Style == HudLayoutStyle.Radial) Radial(canvas, options, view);
            else if (options.Layout.Style == HudLayoutStyle.FourCorner) FourCorner(canvas, options, visual, view);
        }

        sealed class Canvas
        {
            internal readonly HudPanelPlacement Panel;
            internal readonly HudPanelOptions Options;
            internal readonly HudClassicLayout Layout;
            internal readonly HudPanelTheme Theme;
            internal readonly HudDrawList Output;
            internal readonly PixelViewport Viewport;
            internal readonly float Scale;
            internal readonly float Width;
            internal readonly float Height;
            internal readonly HudColor Ink, Dim, Accent, Team, Danger, Background, Rim, Track;

            internal Canvas(in HudPanelPlacement panel, in HudPanelOptions options, in HudPanelVisual visual, HudDrawList output)
            {
                Panel = panel; Options = options; Layout = options.Layout.Classic; Theme = options.Theme; Output = output;
                Viewport = panel.Viewport; Scale = panel.Scale;
                Width = Viewport.Width / Scale; Height = Viewport.Height / Scale;
                Ink = Theme.Text.Opacity(Theme.TextOpacity);
                Dim = Ink.Opacity(.66f);
                Accent = visual.State == HudVisualState.Danger ? Theme.Danger : Theme.Attention;
                Team = Theme.Base.Opacity(Theme.TextOpacity);
                Danger = Theme.Danger.Opacity(Theme.TextOpacity);
                Background = Theme.Background.Opacity(Theme.BackgroundOpacity);
                Rim = Theme.Attention.Opacity(.34f * Theme.TextOpacity);
                Track = Theme.Attention.Opacity(.16f * Theme.TextOpacity);
            }

            internal void Box(float x, float y, float width, float height, HudColor color, string region)
            {
                if (!(width > 0) || !(height > 0) || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(width) || !float.IsFinite(height)) return;
                float px = (Viewport.X - Panel.Bounds.X) + x * Scale;
                float py = (Viewport.Y + Viewport.Height - Panel.Bounds.Y) - (y + height) * Scale;
                Output.Add(new(HudDrawKind.Rectangle, new(px, py, width * Scale, height * Scale), color, "", 0, region));
            }

            internal void Text(float x, float y, float width, float height, string text, float size, HudColor color, string region,
                HudTextAlign align = HudTextAlign.Left, bool numeric = false)
            {
                if (string.IsNullOrEmpty(text) || !(width > 0) || !(height > 0)) return;
                string value = numeric ? text : HudPanelDrawing.Ellipsize(text, width, size);
                if (string.IsNullOrEmpty(value)) value = text;
                float px = (Viewport.X - Panel.Bounds.X) + x * Scale;
                float py = (Viewport.Y + Viewport.Height - Panel.Bounds.Y) - (y + height) * Scale;
                Output.Add(new(HudDrawKind.Text, new(px, py, width * Scale, height * Scale), color, value, size * Scale, region)
                { Numeric = numeric, Align = align });
            }

            // The first argument is the desired right edge; this matches the wireframe's right-aligned columns.
            internal void Right(float right, float y, float width, float height, string text, float size, HudColor color, string region, bool numeric = false)
                => Text(right - width, y, width, height, text, size, color, region, HudTextAlign.Right, numeric);
            internal void Center(float x, float y, float width, float height, string text, float size, HudColor color, string region, bool numeric = false)
                => Text(x, y, width, height, text, size, color, region, HudTextAlign.Center, numeric);

            internal void PanelBox(float x, float y, float width, float height, string region)
            {
                Box(x, y, width, height, Background, region + "-background");
                Box(x, y, 2, height, Accent.Opacity(.82f), region + "-accent");
                Box(x, y, width, 1, Rim, region + "-top");
                Box(x, y + height - 1, width, 1, Rim.Opacity(.55f), region + "-bottom");
                Box(x, y, 1, height, Rim.Opacity(.35f), region + "-left");
                Box(x + width - 1, y, 1, height, Rim.Opacity(.35f), region + "-right");
                Box(x, y, 12, 1, Accent.Opacity(.62f), region + "-tick");
                Box(x, y, 1, 12, Accent.Opacity(.62f), region + "-tick");
            }

            internal void SegBar(float x, float y, float width, float height, int segments, float fill, HudColor on, HudColor off, string region, float gap = 2)
            {
                segments = Math.Clamp(segments, 1, 20);
                if (!(width > 0) || !(height > 0)) return;
                float cell = (width - gap * (segments - 1)) / segments;
                if (!(cell > 0)) return;
                for (int i = 0; i < segments; i++)
                {
                    float bx = x + i * (cell + gap);
                    Box(bx, y, cell, height, off, region + "-track");
                    float part = Math.Clamp(fill - i, 0, 1);
                    if (part > 0) Box(bx, y, cell * part, height, on, region + "-fill");
                }
            }

            internal void ThinBar(float x, float y, float width, float height, float ratio, HudColor on, HudColor off, string region)
            {
                if (!(width > 0) || !(height > 0)) return;
                Box(x, y, width, height, off, region + "-track");
                ratio = float.IsFinite(ratio) ? Math.Clamp(ratio, 0, 1) : 0;
                if (ratio > 0) Box(x, y, width * ratio, height, on, region + "-fill");
            }

            internal void Arc(float cx, float cy, float radius, float from, float to, int segments, float fill,
                float thickness, HudColor on, HudColor off, string region)
            {
                if (!(radius > 0) || !(thickness > 0) || segments <= 0) return;
                float step = (to - from) / segments, gap = Math.Min(1.2f, Math.Abs(step) * .12f);
                for (int i = 0; i < segments; i++)
                {
                    float start = from + i * step + gap * .5f, end = from + (i + 1) * step - gap * .5f;
                    if (end <= start) continue;
                    float part = Math.Clamp(fill - i, 0, 1), split = start + (end - start) * part;
                    if (part > 0) ArcRun(cx, cy, radius, start, split, thickness, on, region + "-fill");
                    if (part < 1) ArcRun(cx, cy, radius, split, end, thickness, off, region + "-track");
                }
            }

            void ArcRun(float cx, float cy, float radius, float from, float to, float thickness, HudColor color, string region)
            {
                float length = Math.Abs((to - from) * MathF.PI / 180) * radius;
                int count = Math.Max(1, (int)Math.Ceiling(length / Math.Max(1, thickness * .9f)));
                float step = (to - from) / count, size = Math.Max(1, thickness);
                for (int i = 0; i < count; i++)
                {
                    float angle = (from + (i + .5f) * step) * MathF.PI / 180;
                    float x = cx + radius * MathF.Cos(angle), y = cy + radius * MathF.Sin(angle);
                    Box(x - size * .5f, y - size * .5f, size, size, color, region);
                }
            }

            internal void Dashed(float x0, float y0, float x1, float y1, HudColor color, string region)
            {
                float dx = x1 - x0, dy = y1 - y0, length = MathF.Sqrt(dx * dx + dy * dy);
                if (!(length > 1) || !float.IsFinite(length)) return;
                int count = Math.Min(12, Math.Max(2, (int)(length / 24)));
                for (int i = 0; i < count; i++)
                {
                    float t = (i + .5f) / count;
                    Box(x0 + dx * t - 1, y0 + dy * t - 1, 2, 2, color, region);
                }
            }

            internal void PointRight(float x, float y, float height, HudColor color, string region)
            {
                Box(x, y + height * .5f - 1, 2, 2, color, region);
                Box(x + 2, y + height * .5f - 3, 2, 2, color, region);
                Box(x + 2, y + height * .5f + 1, 2, 2, color, region);
                Box(x + 4, y + height * .5f - 5, 2, 2, color, region);
                Box(x + 4, y + height * .5f + 3, 2, 2, color, region);
            }

            internal void PointLeft(float x, float y, float height, HudColor color, string region)
            {
                Box(x + 4, y + height * .5f - 1, 2, 2, color, region);
                Box(x + 2, y + height * .5f - 3, 2, 2, color, region);
                Box(x + 2, y + height * .5f + 1, 2, 2, color, region);
                Box(x, y + height * .5f - 5, 2, 2, color, region);
                Box(x, y + height * .5f + 3, 2, 2, color, region);
            }
        }

        readonly record struct Area(float X, float Y, float Width, float Height)
        {
            internal bool Overlaps(in Area other)
                => X < other.X + other.Width && X + Width > other.X && Y < other.Y + other.Height && Y + Height > other.Y;
        }

        static void FourCorner(Canvas c, HudPanelOptions o, HudPanelVisual visual, HudPanelView view)
        {
            var p = view.Personal; var scene = view.Scene; var team = view.Team;
            float margin = c.Layout.Margin, top = c.Layout.Top, bottom = c.Layout.Bottom;
            bool teamVisible = o.Layout.Team && team.Active && c.Width >= 2 * margin + 336 + 320;
            float teamWidth = 336;
            float missionWidth = Math.Min(600, Math.Max(280, c.Width - 2 * margin - (teamVisible ? teamWidth + 24 : 0)));
            var mission = scene.Active && (scene.Mission.Active || scene.Objective.Active) ? (scene.Mission.Active ? scene.Mission : scene.Objective) : default;
            bool missionVisible = mission.Active;
            if (missionVisible)
            {
                float x = margin, y = top;
                c.PanelBox(x, y, missionWidth, 84, "classic-mission");
                c.Text(x + 16, y + 8, 200, 20, "任务", c.Layout.LabelSize, c.Dim, "classic-mission-title");
                c.Text(x + 52, y + 8, 120, 20, "MISSION", Math.Max(8, c.Layout.LabelSize - 2), c.Dim, "classic-mission-caption");
                string text = string.IsNullOrEmpty(mission.Text) ? mission.Title : mission.Text;
                c.Text(x + 16, y + 32, missionWidth - 32, 30, text, c.Layout.BodySize + 3, c.Ink, "classic-mission-text");
                if (scene.Countdown.Active)
                {
                    c.Right(x + missionWidth - 16, y + 28, 130, 24, scene.Countdown.Text, c.Layout.NumberSize - 4, c.Accent, "classic-mission-countdown", true);
                    c.Right(x + missionWidth - 16, y + 54, 130, 18, "倒计时", c.Layout.LabelSize - 1, c.Dim, "classic-mission-caption");
                }
                else if (scene.Scan.Active && scene.Scan.Progress is float scan)
                {
                    c.Right(x + missionWidth - 16, y + 28, 90, 24, HudReadoutFormatting.Percent(scan, 0), c.Layout.NumberSize - 4, c.Accent, "classic-mission-scan", true);
                    c.Right(x + missionWidth - 16, y + 54, 90, 18, "扫描", c.Layout.LabelSize - 1, c.Dim, "classic-mission-caption");
                }
            }
            if (teamVisible)
            {
                float x = c.Width - margin - teamWidth, y = top;
                c.PanelBox(x, y, teamWidth, 48 + 3 * 76, "classic-team");
                c.Text(x + 14, y + 8, 180, 22, "队伍", c.Layout.BodySize, c.Ink, "classic-team-header");
                c.Text(x + 56, y + 9, 80, 20, "SQUAD", Math.Max(8, c.Layout.LabelSize - 1), c.Dim, "classic-team-caption");
                int occupied = 0; for (int i = 0; i < 3; i++) if (!string.IsNullOrEmpty(team[i].Kind)) occupied++;
                c.Right(x + teamWidth - 14, y + 9, 70, 20, occupied + "/3", c.Layout.LabelSize, c.Dim, "classic-team-count", true);
                for (int i = 0; i < 3; i++)
                {
                    var row = team[i]; float ry = y + 48 + i * 76; bool empty = row.Name == "空位" || string.IsNullOrEmpty(row.Kind);
                    if (i > 0) c.Box(x + 14, ry - 8, teamWidth - 28, 1, c.Dim.Opacity(.22f), "classic-team-separator");
                    float? hp = row.HealthFraction;
                    var color = hp is float fraction && fraction <= .3f || row.Priority >= 80 ? c.Danger : c.Team;
                    if (empty) color = c.Dim.Opacity(.45f);
                    c.Box(x + 12, ry + 16, 3, 42, color, "classic-team-rim");
                    c.Text(x + 24, ry + 10, 170, 24, row.Name, c.Layout.BodySize, empty ? c.Dim : c.Ink, "classic-team-name");
                    c.Right(x + teamWidth - 14, ry + 10, 58, 24, empty ? "—" : row.Health, c.Layout.BodySize, color, "classic-team-health", true);
                    c.ThinBar(x + 24, ry + 36, 150, 5, hp ?? 0, color, c.Team.Opacity(.18f), "classic-team-bar");
                    if (!empty)
                    {
                        string status = TeamStatus(row);
                        c.Text(x + 24, ry + 46, teamWidth - 40, 18, status, c.Layout.LabelSize - 1,
                            row.Priority >= 80 ? c.Danger : c.Dim, "classic-team-status");
                    }
                }
            }
            if (p.Active)
            {
                float pw = 420, ph = 288, px = margin, py = c.Height - bottom - ph;
                c.PanelBox(px, py, pw, ph, "classic-personal");
                c.Text(px + 16, py + 8, 130, 22, "个人状态", c.Layout.BodySize - 1, c.Ink, "classic-personal-header");
                c.Text(px + 82, py + 9, 90, 20, "STATUS", Math.Max(8, c.Layout.LabelSize - 1), c.Dim, "classic-personal-caption");
                c.Right(px + pw - 16, py + 9, 120, 20, visual.Label + " · 本地", c.Layout.LabelSize, c.Accent, "classic-personal-state");
                float y = py + 44;
                if (p.Options.Health)
                {
                    c.Box(px + 16, y - 6, pw - 32, 1, c.Dim.Opacity(.2f), "classic-personal-separator");
                    c.Text(px + 16, y + 2, 70, 20, "生命", c.Layout.LabelSize, c.Dim, "classic-personal-health-label");
                    bool low = p.Health.Available && p.Health.Amount <= o.Behavior.LowHealth;
                    var hpColor = low ? c.Danger : c.Ink;
                    c.Right(px + pw - 132, y - 4, 116, 34, view.Health, c.Layout.NumberSize + 4, hpColor, "classic-personal-health", true);
                    c.SegBar(px + 16, y + 34, pw - 32, 10, 10, (float)(p.Health.Amount ?? 0) * 10, low ? c.Danger : c.Accent, c.Ink.Opacity(.16f), "classic-personal-health-bar");
                    y += 58;
                }
                bool infectionVisible = p.Options.Infection && p.ShowInfection && (!p.Infection.Available || p.Infection.Amount is double amount && amount > 0);
                if (infectionVisible)
                {
                    c.Text(px + 16, y + 2, 70, 20, "感染", c.Layout.LabelSize, c.Dim, "classic-personal-infection-label");
                    c.Right(px + pw - 132, y, 116, 24, view.Infection, c.Layout.BodySize + 2, c.Accent, "classic-personal-infection", true);
                    c.SegBar(px + 16, y + 24, pw - 32, 6, 4, (float)(p.Infection.Amount ?? 0) * 4, c.Accent, c.Ink.Opacity(.14f), "classic-personal-infection-bar", 2);
                    y += 42;
                }
                c.Box(px + 16, y + 2, pw - 32, 1, c.Dim.Opacity(.2f), "classic-personal-separator");
                y += 10;
                string life = string.IsNullOrEmpty(p.Life) ? "存活" : p.Life;
                c.Text(px + 16, y, 60, 20, "状态", c.Layout.LabelSize, c.Dim, "classic-personal-life-label");
                c.Text(px + 56, y, 130, 20, life, c.Layout.BodySize, p.Life == "已倒地" || p.Life == "已死亡" ? c.Danger : c.Team, "classic-personal-life");
                y += 28;
                int shown = 0;
                for (int i = 0; i < view.Prompts.Count && i < 2; i++)
                {
                    var prompt = view.Prompts[i]; var color = prompt.Priority >= 85 ? c.Danger : prompt.Priority >= 50 ? c.Accent : c.Dim;
                    bool progress = prompt.Progress is float pg && float.IsFinite(pg) && pg >= 0 && pg <= 1;
                    c.Text(px + 16, y, pw - (progress ? 96 : 32), 20, prompt.Text, c.Layout.LabelSize, color, "classic-personal-action");
                    if (progress)
                    {
                        c.Right(px + pw - 64, y, 48, 20, HudReadoutFormatting.Percent(prompt.Progress.Value, 0), c.Layout.LabelSize, color, "classic-personal-action-value", true);
                        c.ThinBar(px + 16, y + 20, pw - 32, 2, prompt.Progress.Value, color, c.Ink.Opacity(.16f), "classic-personal-action-bar");
                    }
                    y += progress ? 30 : 22; shown++;
                }
                if (shown == 0 && !string.IsNullOrEmpty(p.FocusStatus))
                    c.Text(px + 16, y, pw - 32, 20, p.FocusStatus, c.Layout.LabelSize, c.Dim, "classic-personal-action");
                c.Right(px + pw - 16, py + ph - 20, pw - 32, 16, "交互 / 动作进度", Math.Max(8, c.Layout.LabelSize - 2), c.Dim.Opacity(.8f), "classic-personal-footer");
                DrawEquipment(c, o, visual, view, p);
            }
            FourCornerScene(c, view, margin);
            DrawMarkers(c, view.Scene, margin);
        }

        static void DrawEquipment(Canvas c, HudPanelOptions o, HudPanelVisual visual, HudPanelView view, in HudPersonalPresentation p)
        {
            float margin = c.Layout.Margin, ew = 420, eh = 288, ex = c.Width - margin - ew, ey = c.Height - c.Layout.Bottom - eh;
            c.PanelBox(ex, ey, ew, eh, "classic-equipment");
            c.Text(ex + 16, ey + 8, 120, 22, "装备", c.Layout.BodySize - 1, c.Ink, "classic-equipment-header");
            c.Text(ex + 58, ey + 9, 100, 20, "LOADOUT", Math.Max(8, c.Layout.LabelSize - 1), c.Dim, "classic-equipment-caption");
            c.Right(ex + ew - 16, ey + 9, 110, 20, visual.Label, c.Layout.LabelSize, c.Accent, "classic-equipment-state");
            c.Box(ex + 16, ey + 32, ew - 32, 1, c.Dim.Opacity(.2f), "classic-equipment-separator");
            float y = ey + 42;
            if (p.Options.Weapons)
            {
                EquipmentWeapon(c, p.Primary, "主武器", ex + 16, y, ew - 32, c.Layout.NumberSize, p.FocusSlot == HudEquipmentSlot.Primary, "classic-equipment-primary");
                y += 56;
                EquipmentRow(c, p.Secondary, "副武器", ex + 16, y, ew - 32, c.Layout.BodySize, "classic-equipment-secondary");
            }
            y += 34;
            if (p.Options.Tools) EquipmentTool(c, p.Tool, ex + 16, y, ew - 32, view.ToolFraction);
            y += 54;
            c.Box(ex + 16, y - 6, ew - 32, 1, c.Dim.Opacity(.2f), "classic-equipment-separator");
            if (p.Options.Items)
            {
                float cellWidth = (ew - 32 - 12) / 2;
                DrawItemCell(c, p.Pack, "补给", ex + 16, y, cellWidth, "classic-equipment-pack");
                DrawItemCell(c, p.Consumable, "消耗品", ex + 16 + cellWidth + 12, y, cellWidth, "classic-equipment-consumable");
                DrawItemCell(c, p.Heavy, "重型物品", ex + 16, y + 44, cellWidth, "classic-equipment-heavy");
                DrawItemCell(c, p.Carry, "搬运", ex + 16 + cellWidth + 12, y + 44, cellWidth, "classic-equipment-carry");
            }
        }

        static void EquipmentWeapon(Canvas c, in HudDetailRow row, string label, float x, float y, float width, float number, bool focus, string region)
        {
            c.Text(x, y, width, 18, label, c.Layout.LabelSize, c.Dim, region + "-label");
            string name = row.Visible ? row.Name : HudReadoutFormatting.State(row.State);
            c.Text(x, y + 18, width - 160, 24, name, c.Layout.BodySize + 1, row.Visible ? c.Ink : c.Dim, region + "-name");
            if (focus) c.Box(x - 4, y + 14, 2, 26, c.Accent, region + "-focus");
            if (row.Visible && !string.IsNullOrEmpty(row.Value))
            {
                SplitAmmo(row.Value, out string magazine, out string reserve);
                c.Right(x + width, y + 8, 158, 30, magazine + (string.IsNullOrEmpty(reserve) ? "" : " / " + reserve), number, c.Ink, region + "-value", true);
                c.Right(x + width, y + 38, 158, 14, string.IsNullOrEmpty(reserve) ? row.Label : "弹匣 / 备用", Math.Max(8, c.Layout.LabelSize - 2), c.Dim, region + "-caption");
            }
        }

        static void EquipmentRow(Canvas c, in HudDetailRow row, string label, float x, float y, float width, float size, string region)
        {
            if (!row.Visible) return;
            c.Text(x, y, 60, 20, label, c.Layout.LabelSize - 1, c.Dim, region + "-label");
            c.Text(x + 58, y, width - 180, 20, row.Name, size, c.Ink, region + "-name");
            if (!string.IsNullOrEmpty(row.Value)) c.Right(x + width, y, 114, 20, row.Value, size, c.Dim, region + "-value", true);
        }

        static void EquipmentTool(Canvas c, in HudDetailRow row, float x, float y, float width, float? fraction)
        {
            c.Text(x, y, 60, 20, "工具", c.Layout.LabelSize - 1, c.Dim, "classic-equipment-tool-label");
            if (!row.Visible)
            {
                c.Text(x + 58, y, width - 58, 20, HudReadoutFormatting.State(row.State), c.Layout.BodySize, c.Dim, "classic-equipment-tool-name");
                return;
            }
            c.Text(x + 58, y, width - 170, 20, row.Name, c.Layout.BodySize, c.Ink, "classic-equipment-tool-name");
            if (!string.IsNullOrEmpty(row.Value)) c.Right(x + width, y, 108, 20, row.Value, c.Layout.BodySize, c.Accent, "classic-equipment-tool-value", true);
            c.ThinBar(x + 58, y + 22, 150, 5, fraction ?? 0, c.Accent, c.Ink.Opacity(.14f), "classic-equipment-tool-bar");
        }

        static void DrawItemCell(Canvas c, in HudDetailRow row, string label, float x, float y, float width, string region)
        {
            if (!row.Visible) return;
            c.Box(x, y, width, 38, c.Background.Opacity(.55f), region + "-cell");
            c.Text(x + 6, y + 3, width - 12, 15, label, Math.Max(8, c.Layout.LabelSize - 2), c.Dim, region + "-label");
            c.Text(x + 6, y + 17, width - 12, 18, row.Name, Math.Max(9, c.Layout.LabelSize), c.Ink, region + "-name");
            string value = string.IsNullOrEmpty(row.Value) ? "持有" : row.Value;
            c.Right(x + width - 6, y + 17, width * .52f, 18, value, Math.Max(9, c.Layout.LabelSize), c.Dim, region + "-value", true);
        }

        static void FourCornerScene(Canvas c, HudPanelView view, float margin)
        {
            var scene = view.Scene;
            if (!scene.Active) return;
            bool missionVisible = scene.Mission.Active || scene.Objective.Active;
            if (scene.Scan.Active && scene.Scan.Progress is float scan)
            {
                float x = c.Width * .5f - 150;
                c.Text(x, 42, 248, 20, scene.Scan.Title, c.Layout.LabelSize, c.Ink, "classic-scan-title");
                c.Right(x + 250, 42, 50, 20, HudReadoutFormatting.Percent(scan, 0), c.Layout.LabelSize, c.Accent, "classic-scan-value", true);
                c.ThinBar(x, 66, 300, 3, scan, c.Accent, c.Ink.Opacity(.2f), "classic-scan-bar");
            }
            if (!missionVisible && scene.Countdown.Active) c.Center(c.Width * .5f - 150, 84, 300, 22, scene.Countdown.Title + "  " + scene.Countdown.Text, c.Layout.BodySize, c.Accent, "classic-countdown");
            if (scene.Interaction.Active)
            {
                var card = scene.Interaction; float x = c.Width * .5f - 180, y = c.Height * .5f + 64;
                c.PanelBox(x, y, 360, 72, "classic-interaction");
                c.Text(x + 12, y + 8, 336, 20, card.Title, c.Layout.LabelSize, c.Accent, "classic-interaction-title");
                c.Text(x + 12, y + 30, 240, 22, card.Text, c.Layout.BodySize, c.Ink, "classic-interaction-text");
                c.Right(x + 348, y + 30, 110, 20, card.Secondary, c.Layout.LabelSize, c.Dim, "classic-interaction-key");
                if (card.Progress is float progress)
                {
                    c.ThinBar(x + 12, y + 58, 336, 2, progress, c.Accent, c.Ink.Opacity(.18f), "classic-interaction-bar");
                    c.Right(x + 348, y + 44, 80, 16, HudReadoutFormatting.Percent(progress, 0), c.Layout.LabelSize, c.Accent, "classic-interaction-value", true);
                }
            }
        }

        static void Radial(Canvas c, HudPanelOptions o, HudPanelView view)
        {
            var p = view.Personal; var scene = view.Scene; var team = view.Team;
            var origin = c.Panel.StyleOrigin;
            float cx = (origin.X - c.Viewport.X) / c.Scale;
            float cy = (c.Viewport.Y + c.Viewport.Height - origin.Y) / c.Scale;
            if (!float.IsFinite(cx) || !float.IsFinite(cy)) { cx = c.Width * .5f; cy = c.Height * .62f; }
            float radius = 170 * c.Layout.RadialScale;
            DrawTicks(c, cx, cy, radius + 3);
            if (p.Options.Health)
            {
                float health = p.Health.Amount is double hp ? (float)Math.Clamp(hp, 0, 1) : 0;
                var healthColor = health <= o.Behavior.LowHealth ? c.Danger : c.Ink;
                c.Arc(cx, cy, radius, 105, 168, 10, health * 10, 8, healthColor, c.Track, "classic-radial-health");
                c.Right(cx - 244, cy + 104, 86, 32, view.Health, c.Layout.NumberSize + 2, healthColor, "classic-radial-health-value", true);
                c.Right(cx - 244, cy + 84, 86, 18, "生命", c.Layout.LabelSize, c.Dim, "classic-radial-health-label");
            }
            bool infectionVisible = p.Options.Infection && p.ShowInfection && (!p.Infection.Available || p.Infection.Amount is double amount && amount > 0);
            if (infectionVisible)
            {
                float infection = p.Infection.Amount is double value ? (float)Math.Clamp(value, 0, 1) : 0;
                c.Arc(cx, cy, radius, 12, 75, 4, infection * 4, 6, c.Accent, c.Track, "classic-radial-infection");
                c.Text(cx + 158, cy + 104, 86, 24, view.Infection, c.Layout.BodySize + 3, c.Accent, "classic-radial-infection-value", HudTextAlign.Left, true);
                c.Text(cx + 158, cy + 84, 86, 18, "感染", c.Layout.LabelSize, c.Dim, "classic-radial-infection-label");
            }
            string life = string.IsNullOrEmpty(p.Life) ? "存活" : p.Life;
            var lifeColor = p.Life == "已倒地" || p.Life == "已死亡" ? c.Danger : c.Team;
            string status = life;
            HudPrompt prompt = default;
            if (view.Prompts.Count > 0)
            {
                prompt = view.Prompts[0];
                if (!string.IsNullOrEmpty(prompt.Text)) status += " · " + prompt.Text;
            }
            c.Center(cx - 190, cy + 168, 380, 24, status, c.Layout.BodySize, lifeColor, "classic-radial-status");
            if (prompt.Progress is float progress)
            {
                c.ThinBar(cx - 100, cy + 196, 200, 3, progress, c.Accent, c.Ink.Opacity(.18f), "classic-radial-action-bar");
                c.Center(cx - 190, cy + 202, 380, 16, HudReadoutFormatting.Percent(progress, 0), c.Layout.LabelSize, c.Accent, "classic-radial-action-value", true);
            }
            DrawRadialEquipment(c, view, cx, cy);
            DrawRadialMission(c, scene, cx, cy);
            if (o.Layout.Team && team.Active) DrawRadialTeam(c, team, cx, cy);
            DrawRadialScan(c, scene);
            DrawRadialInteraction(c, scene);
            DrawRadialPrompts(c, view, cx, cy);
            DrawMarkers(c, scene, c.Layout.Margin);
        }

        static void DrawTicks(Canvas c, float cx, float cy, float radius)
        {
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30 * MathF.PI / 180;
                float x = cx + radius * MathF.Cos(angle), y = cy + radius * MathF.Sin(angle);
                c.Box(x - 1, y - 3, 2, 6, c.Dim.Opacity(.3f), "classic-radial-tick");
            }
        }

        static void DrawRadialEquipment(Canvas c, HudPanelView view, float cx, float cy)
        {
            var p = view.Personal; float x = cx - 350, y = cy + 230, w = 700, h = 90;
            x = Math.Clamp(x, c.Layout.Margin, Math.Max(c.Layout.Margin, c.Width - c.Layout.Margin - w));
            c.PanelBox(x, y, w, h, "classic-radial-equipment");
            if (p.Options.Weapons)
            {
                var row = p.Primary;
                string weaponLabel = "主武器";
                if (p.FocusVisible && p.FocusSlot is HudEquipmentSlot.Primary or HudEquipmentSlot.Secondary)
                {
                    row = p.FocusSlot == HudEquipmentSlot.Secondary ? p.Secondary : p.Primary;
                    weaponLabel = p.FocusSlot == HudEquipmentSlot.Secondary ? "副武器" : "主武器";
                }
                c.Text(x + 20, y + 10, 120, 18, weaponLabel, Math.Max(8, c.Layout.LabelSize - 1), c.Dim, "classic-radial-equipment-label");
                string name = row.Visible ? row.Name : HudReadoutFormatting.State(row.State);
                c.Text(x + 20, y + 28, 190, 24, name, c.Layout.BodySize, row.Visible ? c.Ink : c.Dim, "classic-radial-equipment-name");
                if (!string.IsNullOrEmpty(row.Value))
                {
                    SplitAmmo(row.Value, out string magazine, out string reserve);
                    c.Right(x + 344, y + 20, 170, 34, magazine + (string.IsNullOrEmpty(reserve) ? "" : " / " + reserve), c.Layout.NumberSize - 2, c.Ink, "classic-radial-equipment-value", true);
                    c.Right(x + 344, y + 54, 170, 16, string.IsNullOrEmpty(reserve) ? row.Label : "弹匣 / 备用", Math.Max(8, c.Layout.LabelSize - 2), c.Dim, "classic-radial-equipment-caption");
                }
            }
            c.Box(x + 350, y + 14, 1, h - 28, c.Dim.Opacity(.28f), "classic-radial-equipment-divider");
            if (p.Options.Tools)
            {
                c.Text(x + 368, y + 10, 72, 18, "工具", Math.Max(8, c.Layout.LabelSize - 1), c.Dim, "classic-radial-tool-label");
                c.Text(x + 368, y + 28, 118, 20, p.Tool.Visible ? p.Tool.Name : HudReadoutFormatting.State(p.Tool.State), Math.Max(9, c.Layout.LabelSize), c.Ink, "classic-radial-tool-name");
                if (!string.IsNullOrEmpty(p.Tool.Value)) c.Right(x + 490, y + 28, 64, 20, p.Tool.Value, Math.Max(9, c.Layout.LabelSize), c.Accent, "classic-radial-tool-value", true);
            }
            if (p.Options.Items)
            {
                DrawRadialItem(c, p.Pack, "补给", x + 500, y + 10);
                DrawRadialItem(c, p.Consumable, "消耗品", x + 592, y + 10);
                DrawRadialItem(c, p.Heavy, "重型", x + 500, y + 46);
                DrawRadialItem(c, p.Carry, "搬运", x + 592, y + 46);
            }
        }

        static void DrawRadialItem(Canvas c, in HudDetailRow row, string label, float x, float y)
        {
            c.Text(x, y, 82, 14, label, Math.Max(8, c.Layout.LabelSize - 2), c.Dim, "classic-radial-item-label");
            if (!row.Visible) { c.Text(x, y + 14, 82, 16, "—", Math.Max(8, c.Layout.LabelSize - 1), c.Dim.Opacity(.5f), "classic-radial-item"); return; }
            c.Text(x, y + 14, 82, 16, row.Name, Math.Max(8, c.Layout.LabelSize - 1), c.Ink, "classic-radial-item-name");
            if (!string.IsNullOrEmpty(row.Value)) c.Right(x + 82, y + 14, 82, 16, row.Value, Math.Max(8, c.Layout.LabelSize - 1), c.Dim, "classic-radial-item-value", true);
        }

        static void DrawRadialMission(Canvas c, HudScenePresentation scene, float cx, float cy)
        {
            if (!scene.Active || !(scene.Mission.Active || scene.Objective.Active)) return;
            var card = scene.Mission.Active ? scene.Mission : scene.Objective;
            float w = 320, h = 64, x = cx - 508, y = cy - 450;
            x = Math.Clamp(x, c.Layout.Margin, Math.Max(c.Layout.Margin, c.Width - c.Layout.Margin - w));
            y = Math.Clamp(y, c.Layout.Margin, Math.Max(c.Layout.Margin, c.Height - c.Layout.Margin - h));
            c.PanelBox(x, y, w, h, "classic-radial-mission");
            c.Text(x + 16, y + 8, 250, 18, "任务", Math.Max(8, c.Layout.LabelSize - 1), c.Dim, "classic-radial-mission-caption");
            c.Text(x + 16, y + 28, w - 50, 26, string.IsNullOrEmpty(card.Text) ? card.Title : card.Text, c.Layout.BodySize + 1, c.Ink, "classic-radial-mission-text");
            c.PointRight(x + w - 5, y, h, c.Accent, "classic-radial-mission-point");
            c.Dashed(x + w, y + h * .5f, cx - 44, cy - 98, c.Rim, "classic-radial-mission-line");
        }

        static void DrawRadialTeam(Canvas c, HudTeamPresentation team, float cx, float cy)
        {
            for (int i = 0; i < 3; i++)
            {
                var row = team[i]; bool empty = row.Name == "空位" || string.IsNullOrEmpty(row.Kind);
                float w = 190, h = 58, x = cx + 326, y = cy - 464 + i * 74;
                x = Math.Clamp(x, c.Layout.Margin, Math.Max(c.Layout.Margin, c.Width - c.Layout.Margin - w));
                y = Math.Clamp(y, c.Layout.Margin, Math.Max(c.Layout.Margin, c.Height - c.Layout.Margin - h));
                c.PanelBox(x, y, w, h, "classic-radial-team");
                c.PointLeft(x + 1, y, h, c.Accent, "classic-radial-team-point");
                float? hp = row.HealthFraction;
                var color = hp is float fraction && fraction <= .3f || row.Priority >= 80 ? c.Danger : c.Team;
                if (empty) color = c.Dim.Opacity(.45f);
                c.Box(x + 22, y + 10, 2, 38, color, "classic-radial-team-rim");
                c.Text(x + 30, y + 8, 112, 20, row.Name, Math.Max(9, c.Layout.LabelSize), empty ? c.Dim : c.Ink, "classic-radial-team-name");
                c.Right(x + w - 12, y + 8, 48, 20, empty ? "—" : row.Health, Math.Max(9, c.Layout.LabelSize), color, "classic-radial-team-health", true);
                c.ThinBar(x + 30, y + 29, 118, 4, hp ?? 0, color, c.Team.Opacity(.18f), "classic-radial-team-bar");
                if (!empty)
                {
                    string status = TeamStatus(row);
                    c.Text(x + 30, y + 38, 148, 16, status, Math.Max(8, c.Layout.LabelSize - 2), row.Priority >= 80 ? c.Danger : c.Dim, "classic-radial-team-status");
                }
                c.Dashed(x, y + h * .5f, cx + 30 + i * 12, cy - 82 + i * 10, c.Rim, "classic-radial-team-line");
            }
        }

        static void DrawRadialScan(Canvas c, HudScenePresentation scene)
        {
            if (!scene.Active) return;
            if (scene.Scan.Active && scene.Scan.Progress is float scan)
            {
                float x = c.Width * .5f - 150;
                c.Text(x, 42, 248, 20, scene.Scan.Title, c.Layout.LabelSize, c.Ink, "classic-radial-scan-title");
                c.Right(x + 250, 42, 50, 20, HudReadoutFormatting.Percent(scan, 0), c.Layout.LabelSize, c.Accent, "classic-radial-scan-value", true);
                c.ThinBar(x, 66, 300, 3, scan, c.Accent, c.Ink.Opacity(.2f), "classic-radial-scan-bar");
            }
            if (scene.Countdown.Active) c.Center(c.Width * .5f - 150, 84, 300, 22, scene.Countdown.Title + "  " + scene.Countdown.Text, c.Layout.BodySize, c.Accent, "classic-radial-countdown");
        }

        static void DrawRadialInteraction(Canvas c, HudScenePresentation scene)
        {
            if (!scene.Active || !scene.Interaction.Active) return;
            var card = scene.Interaction;
            float ccx = c.Width * .5f, ccy = c.Height * .5f;
            float fill = card.Progress is float progress && float.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
            c.Arc(ccx, ccy, 34, 200, 340, 20, fill * 20, 2, c.Accent, c.Track, "classic-radial-interaction-ring");
            c.Center(ccx - 180, ccy + 46, 360, 20, card.Text, c.Layout.BodySize, c.Ink, "classic-radial-interaction-text");
            if (!string.IsNullOrEmpty(card.Secondary)) c.Center(ccx - 180, ccy + 66, 360, 18, card.Secondary, c.Layout.LabelSize, c.Dim, "classic-radial-interaction-key");
            if (card.Progress is float pg) c.Center(ccx - 180, ccy + 86, 360, 16, HudReadoutFormatting.Percent(pg, 0), c.Layout.LabelSize, c.Accent, "classic-radial-interaction-value", true);
        }

        static void DrawRadialPrompts(Canvas c, HudPanelView view, float cx, float cy)
        {
            // First prompt already lives in the status row. Keep the remaining validated
            // warnings in the free lower-left corner; skip them instead of drawing over panels.
            float equipmentX = cx - 350, equipmentY = cy + 230;
            for (int i = 1; i < view.Prompts.Count && i < 3; i++)
            {
                var prompt = view.Prompts[i]; float y = c.Height - c.Layout.Margin - (view.Prompts.Count - i) * 24;
                if (y + 24 > equipmentY && y < equipmentY + 90 && c.Layout.Margin < equipmentX + 700) continue;
                var color = prompt.Priority >= 85 ? c.Danger : prompt.Priority >= 50 ? c.Accent : c.Dim;
                c.Text(c.Layout.Margin, y, 340, 22, prompt.Text, c.Layout.LabelSize, color, "classic-radial-notification");
            }
        }

        static void DrawMarkers(Canvas c, HudScenePresentation scene, float margin)
        {
            if (scene.Count <= 0) return;
            Area[] blocked = new Area[10]; int count = 0;
            void Block(float x, float y, float w, float h) { if (count < blocked.Length) blocked[count++] = new(x, y, w, h); }
            if (c.Options.Layout.Style == HudLayoutStyle.FourCorner)
            {
                Block(margin, margin, Math.Min(600, c.Width - 2 * margin), 84);
                Block(c.Width - margin - 336, margin, 336, 276);
                Block(margin, c.Height - margin - 288, 420, 288);
                Block(c.Width - margin - 420, c.Height - margin - 288, 420, 288);
                Block(c.Width * .5f - 180, c.Height * .5f + 64, 360, 72);
                Block(c.Width * .5f - 160, 30, 320, 50);
            }
            else
            {
                float ox = (c.Panel.StyleOrigin.X - c.Viewport.X) / c.Scale;
                float oy = (c.Viewport.Y + c.Viewport.Height - c.Panel.StyleOrigin.Y) / c.Scale;
                Block(ox - 508, oy - 450, 320, 64);
                Block(ox + 326, oy - 464, 190, 220);
                Block(ox - 350, oy + 230, 700, 90);
                Block(ox - 190, oy + 168, 380, 48);
                // Keep scene markers away from the inner vitals cluster and its labels.
                Block(ox - 320, oy - 120, 640, 430);
                Block(ox - 390, oy + 40, 390, 150);
                Block(c.Width * .5f - 160, 30, 320, 70);
            }
            Block(c.Width * .5f - 34, c.Height * .5f - 34, 68, 68);
            for (int i = 0; i < scene.Count && i < 3; i++)
            {
                var marker = scene[i]; var b = marker.Bounds;
                float x = (b.X - c.Viewport.X) / c.Scale;
                float y = (c.Viewport.Y + c.Viewport.Height - b.Y - b.Height) / c.Scale;
                float w = b.Width / c.Scale, h = b.Height / c.Scale;
                var area = new Area(x, y, w, h);
                if (x < margin || y < margin || x + w > c.Width - margin || y + h > c.Height - margin) continue;
                bool blockedArea = false;
                for (int k = 0; k < count; k++) if (area.Overlaps(blocked[k])) { blockedArea = true; break; }
                if (blockedArea) continue;
                c.Box(x, y, w, h, c.Background.Opacity(.72f), "classic-marker-background");
                c.Text(x + 5, y + 2, w - 10, h - 4, marker.Text, c.Layout.LabelSize, marker.Kind == HudSceneMarkerKind.Rescue ? c.Danger : c.Team, "classic-marker");
                Block(x, y, w, h);
            }
        }

        static string TeamStatus(HudTeamRow row)
        {
            string status = string.IsNullOrEmpty(row.Cue) ? row.Status : row.Cue;
            if (!string.IsNullOrEmpty(row.Infection) && row.Infection != "—") status = "感染 " + row.Infection + (string.IsNullOrEmpty(status) ? "" : " · " + status);
            if (!string.IsNullOrEmpty(row.Kind)) status += (string.IsNullOrEmpty(status) ? "" : " · ") + row.Kind;
            return status;
        }

        static void SplitAmmo(string value, out string magazine, out string reserve)
        {
            int slash = value?.IndexOf(" / ", StringComparison.Ordinal) ?? -1;
            if (slash >= 0) { magazine = value.Substring(0, slash); reserve = value.Substring(slash + 3); }
            else { magazine = value ?? "—"; reserve = ""; }
        }
    }
}
