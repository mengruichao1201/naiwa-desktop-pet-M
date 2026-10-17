# 动作素材说明

生成方式：Codex 内置 imagegen。身份参考：项目根目录的 `奶娃透明立绘.png`。原待机立绘和捧腹素材未重绘。以下是新增动作使用的最终提示词。白色背景由桌宠在加载时进行外缘连通透明处理。

## run-sheet.png

Production 2D game sprite sheet rendered in smooth matte 3D style. Use the supplied Naiwa image for exact character identity and proportions: small green-eyed head attached to huge yellow pear-shaped torso, giant cream belly, dark olive hands and feet. EXACT three columns and three rows, nine equally sized square cells, full 1536 by 1536 canvas, no borders or labels. Background must be UNIFORMLY PURE WHITE #FFFFFF, including between legs and arms. No shadows, no gradient, no glow, no effects, no colored marks or speckles. Render precisely nine complete separate characters fully inside their cells with at least 35px white margins. First top-left cell is original neutral standing pose for calibration. Other eight in reading order show sequential phases of a natural running stride towards LEFT with left/right legs alternating in full stride, bent knees and counter-swinging arms. Use same camera distance, body size, ground baseline and exact character throughout. Keep tiny head, enormous belly, three-quarter LEFT view. Run cycle phases 0,45,90,135,180,225,270,315 degrees. Do not duplicate starting phase as the final frame. All body parts visible and entirely inside each cell. Not chibi, no text, no outlines, no gridlines, no drop shadow, no cast shadow. Flat pure white backdrop only.

## battle-sheet.png

基于同一立绘生成：3 列 × 2 行的六个完整角色姿态。第 0 格为原始站姿，第 1 格屈膝并开始抬手，第 2 格两腿分开屈膝、双手抬起的战斗架势，第 3 格相同战斗架势的轻微呼吸变化，第 4 格收手起身，第 5 格恢复站姿。保持小头、大肚、黄身体、奶油色肚皮、绿色眼睛、深橄榄色手脚、相同三分之四左向视角和角色比例。

最终背景处理提示词：Edit this sprite sheet. Replace the entire brown and gold background with uniform PURE WHITE (#FFFFFF). Remove glow and ground shadows completely. Keep only the six yellow character poses exactly as they are, with the same poses, proportions, matte shading, positions, sizes and canvas layout, three columns by two rows. Preserve full 1536 by 1024 canvas and equal 512 by 512 cells; do not crop or shift. Clean white empty space between all characters and between their limbs. Opaque white outside the character silhouettes. No checkerboard, transparency, glow, speckles, text or extra characters. Keep all heads, hands and feet intact.
