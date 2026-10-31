# 战斗 UI 美术提示词

> 状态：待生成 · 日期：2026-10-30 · 用途：用户自行生成两张补充 UI 底板，不生成整屏界面。

## 统一要求

参考现有水城战斗背景、深色冷却槽和 `Ui_ActionBtn.png`，保持明亮低多边形奇幻风格。新底板只用于小面积信息，中央为低纹理深青灰，文字另由游戏绘制。不要把回合、数字、图标画进图里，也不要生成一整张游戏截图。

每张单独生成 PNG，透明背景、正视图、无透视。若模型不能输出真实透明背景，使用纯品红 `#FF00FF` 背景，后续统一抠图；不要生成棋盘格假透明。四周留至少 24 px 透明边，完整保留外轮廓。

## 1. 小型回合标识底板（优先）

- 文件名：`Ui_PhaseBadge.png`
- 建议画布：1200×240；主体有效比例约 6.8:1，最终显示约 300×44。
- 文字安全区：主体中央 82% 宽、70% 高；端部装饰限制在左右各 8%。
- 用途：顶部小标识，可显示“第 3 回合 · 你的进攻”。生成图中不要出现这句话。

```text
Create ONE production-ready 2D fantasy game UI phase label background, isolated on a true transparent PNG background. Canvas 1200 by 240 pixels. Straight-on orthographic view, perfectly horizontal, centered, complete silhouette, at least 24 pixels of clear padding around the object. The object is a compact narrow plaque with an effective width-to-height ratio about 6.8:1.

Art direction: bright low-poly magical water-city adventure, crisp polygonal stone-and-metal construction, restrained faceted surfaces, clean charcoal-teal central face, slim silver-gray rim, one subtle pale turquoise highlight on each short beveled end. Small and functional, not a grand banner. No scrolls, no hanging fabric, no elaborate gold ornament, no large crystals.

Reserve the central 82 percent of the width and 70 percent of the height as a plain dark low-texture text-safe area. All decorative geometry stays at the far ends. The long upper and lower edges must remain simple and straight so the middle can stretch horizontally using nine-slice scaling. The text-safe center must give strong contrast to white text added later by the game engine.

No text, no letters, no Chinese characters, no numbers, no icons, no embedded symbols, no watermark, no logo, no scene, no character, no card, no cast shadow outside the object, no bloom, no surrounding glow, no checkerboard pattern, no perspective, no mockup sheet. Render only one isolated blank UI plaque.
```

## 2. 攻防战况底板（优先）

- 文件名：`Ui_CombatReadout.png`
- 建议画布：1360×320；主体有效比例约 5.9:1；以九宫格显示为 340×56 或 340×58。
- 文字安全区：主体中央 88% 宽、74% 高；空白区域需要能容纳一至两行文字。
- 两行战况分别由两个 Image 使用同一张图；不要把两块画在同一张素材中。

```text
Create ONE blank combat information plate for a polished 2D fantasy card battle game. Transparent PNG, 1360 by 320 pixel canvas. Center one complete horizontally elongated plate with an effective aspect ratio about 5.9:1, front view, no perspective, at least 24 pixels of transparent padding on all sides.

Match a bright low-poly magical water-city environment and compact dark fantasy cooldown-slot UI. Use a quiet deep charcoal with a restrained green-teal undertone for the central surface, a very thin cool stone-gray bevel, sharply readable outer contour, small clipped corners. Keep the design flatter and less decorative than an action button: this is a non-interactive numerical readout. Subtle light on the upper rim, no glow, no ornament in the middle.

The central 88 percent of the width and 74 percent of the height must be a clean uniform dark field for white text and warm pale yellow numbers that will be rendered separately. It must support either one or two lines of text. All four corners should be small and consistent. Straight edges and a uniform center must allow nine-slice stretching both horizontally and vertically without distortion.

No text, no numbers, no sword, no shield, no plus or minus sign, no icons, no central gem, no bars or progress meters, no logo, no watermark, no scene, no character, no cards, no outer cast shadow, no bloom, no checkerboard fake transparency, no perspective, no multi-asset sheet. Exactly one blank standalone UI plate.
```

## 不需要额外生成

结束进攻按钮继续用现有 `Ui_ActionBtn.png`；选择提示沿用 `Ui_StageCaption.png`。角色受伤、治疗、光环和冷却变化使用短文字反馈与现有动画，不需要每种消息各生成一张底板。生命宝石、光环徽章等属于后续美术精修，不阻塞本次信息布局。

## 导入检查

生成后的实际像素未必正好符合提示词，先检查透明边缘、中心留白和缩小后的可读性。Unity 导入设为 Sprite / Single，Alpha Is Transparency，Clamp；按最终裁切后的轮廓设九宫格 Border，避免把端部装饰拉伸。最终小尺寸看不清的细节应删除。主色过亮、中央纹理过密或烘焙文字的结果需要重新生成。
