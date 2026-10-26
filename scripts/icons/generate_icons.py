#!/usr/bin/env python3
"""从设计母版生成多分辨率应用图标。

母版是 3000x3000 的不透明白底 PNG。直接把它塞进 .ico 只会产出单一 32x32 帧，
Windows 在任务栏 / 资源管理器 / 托盘里只能靠拉伸插值来放大，视觉上必然模糊。

本脚本做四件事：

1. 自动裁边：按非白内容的包围盒裁掉四周留白，再补 6% 呼吸空间。
   小尺寸（尤其托盘 16x16）下每一像素都很宝贵，白边会白白吃掉有效面积。
2. 抠背景：从图像四边做 flood fill，只把与边界连通的近白像素设为透明。
   注意不能简单地"所有白色转透明"——本图中央的白色文字与白色元素是设计的一部分，
   无差别抠除会在图形上打出破洞。限定边界连通性可以只去掉真正的背景。
3. 羽化：对新产生的透明边缘做 1px 抗锯齿过渡，避免生硬锯齿。
4. 分层输出：每一档尺寸都从母图独立 LANCZOS 降采样（不是先生成小图再放大）。

用法：
    python scripts/icons/generate_icons.py
    python scripts/icons/generate_icons.py --source <母版.png> --assets <Assets 目录>
"""

from __future__ import annotations

import argparse
import sys
from collections import deque
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

# 母版与输出目录的默认位置（相对仓库根）
REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SOURCE = REPO_ROOT / "assets" / "logo_source.png"
DEFAULT_ASSETS = REPO_ROOT / "src" / "MarketAssistant.App" / "Assets"

# 视为"背景白"的阈值：像素三通道的最小值 >= 该值即视为近白
WHITE_THRESHOLD = 235

# 四周呼吸空间占**最终画布**的比例。
#
# 取 0（满出血），与 ZCode 官方图标集（packages/desktop/build/icons）一致：
# 实测其 16/32/48/256/1024 各档左边距均为 0px，内容占满画布，视觉重心最大。
# 此类应用图标靠**圆角切边**产生视觉留白，而非真的留透明边。
#
# 曾尝试 10%~14% 留白，但那是 macOS icns 的内缩建议，直接套用到 Windows
# 任务栏/托盘后图标明显偏小——小尺寸下每一格都很宝贵，白色留白等于浪费像素。
#
# 如需改为带透明边：调大此值即可（分母是最终画布，不是内容边长）。
PADDING_RATIO = 0.0

# 圆角矩形半径，占边长的比例。取 22% 接近 Apple 风格连续曲率观感。
CARD_CORNER_RATIO = 0.22

# Windows exe / 快捷方式 / 资源管理器大图标所需的完整尺寸阶梯
ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)

# 托盘专用：只保留小尺寸档位，减少体积（Windows 会挑最接近的一档）
TRAY_ICO_SIZES = (16, 20, 24, 32, 48, 64)

# UI 内展示用（导航栏 48px、About 页 80px，512 足够覆盖高分屏）
UI_PNG_SIZE = 512

# Linux hicolor 主题图标尺寸，以及 macOS icns 需要的最大边长
LINUX_PNG_SIZE = 256
MACOS_PNG_SIZE = 1024


def load_source(path: Path) -> Image.Image:
    """读取母版并统一为 RGBA。"""
    if not path.is_file():
        raise FileNotFoundError(f"找不到母版图：{path}")
    return Image.open(path).convert("RGBA")


def crop_to_content(image: Image.Image) -> Image.Image:
    """按非白内容裁边，等比缩为正方形，再补上四周一致的呼吸空间。

    母版四周有约 15% 的纯白留白，托盘等小尺寸场景下这部分会显著缩小图形的相对占比。

    注意两个易错点：
    1. 内容往往是长方形（如 446x460）。必须等比缩放成正方形，不能直接拉伸，
       否则圆形遮罩下会变形。
    2. padding 要用**缩放后边长**来算。若沿用原始 max(size)，
       宽方向的留白会被额外放大、而高度方向更紧，导致四边留白不一致。
    """
    rgb = np.asarray(image.convert("RGB")).astype(np.int16)
    near_white = rgb.min(axis=2) >= WHITE_THRESHOLD
    if near_white.all():
        raise ValueError("母版整张都是背景白，没有可用的内容")

    rows = np.flatnonzero(~near_white.all(axis=1))
    cols = np.flatnonzero(~near_white.all(axis=0))
    top, bottom = int(rows[0]), int(rows[-1]) + 1
    left, right = int(cols[0]), int(cols[-1]) + 1
    cropped = image.crop((left, top, right, bottom))

    # 1) 等比缩放到正方形：以较短边为准填充较长边，保持内容比例不失真
    side = max(cropped.size)
    scaled = cropped.resize((side, side), Image.LANCZOS)

    # 2) 四周补等量留白。按"留白占最终画布"反推画布边长，
    #    这样 PADDING_RATIO 的语义就是肉眼看到的画布留白比例（四边严格一致）。
    content_ratio = 1 - 2 * PADDING_RATIO
    final_side = round(side / content_ratio)
    offset = (final_side - side) // 2
    canvas = Image.new("RGBA", (final_side, final_side), (0, 0, 0, 0))
    canvas.alpha_composite(scaled, (offset, offset))
    return canvas



def apply_card_rounding(image: Image.Image) -> Image.Image:
    """给白底卡片加圆角，把四角的白色切成透明。

    母版的白底卡片是带直角的方形，若直接抠掉外围背景，卡片会变成一个
    直角白块贴在画布上——这正是"内容贴边"的观感来源。加圆角后卡片
    与透明背景之间形成明确的轮廓，在任务栏、托盘等任意底色上都更协调。
    """
    side = image.width
    radius = max(1, round(side * CARD_CORNER_RATIO))

    # 先放大 4 倍绘制遮罩再降采样，避免圆角边缘出现阶梯锯齿
    scale = 4
    mask = Image.new("L", (side * scale, side * scale), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, side * scale - 1, side * scale - 1], radius=radius * scale, fill=255
    )
    mask = mask.resize((side, side), Image.LANCZOS)

    result = image.copy()
    # 把圆角遮罩乘进原 alpha：保留遮罩内的原始透明度，实现平滑过渡
    alpha = result.getchannel("A")
    result.putalpha(ImageChops.multiply(alpha, mask))
    return result


def remove_border_connected_background(image: Image.Image) -> Image.Image:
    """把与图像边界连通的近白像素设为透明，保留图形内部的白色元素。

    关键点：只处理"从四边出发能走到的"近白像素。若对全图做阈值抠除，
    图中央的白色文字和白色图形会被一起抠掉，logo 会明显残缺。
    """
    rgba = np.asarray(image).astype(np.uint8).copy()
    height, width = rgba.shape[:2]
    near_white = rgba[:, :, :3].min(axis=2) >= WHITE_THRESHOLD

    background = np.zeros((height, width), dtype=bool)
    queue: deque[tuple[int, int]] = deque()

    def enqueue(y: int, x: int) -> None:
        if near_white[y, x] and not background[y, x]:
            background[y, x] = True
            queue.append((y, x))

    for x in range(width):
        enqueue(0, x)
        enqueue(height - 1, x)
    for y in range(height):
        enqueue(y, 0)
        enqueue(y, width - 1)

    while queue:
        y, x = queue.popleft()
        for ny, nx in ((y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)):
            if 0 <= ny < height and 0 <= nx < width:
                enqueue(ny, nx)

    rgba[background, 3] = 0
    return Image.fromarray(rgba, mode="RGBA")


def feather_alpha(image: Image.Image) -> Image.Image:
    """对 alpha 通道做羽化，消除抠图产生的硬锯齿。

    只模糊 alpha、不动 RGB：若连 RGB 一起模糊，边缘像素会与透明区域取平均，
    从而在图标周围产生一圈暗边（黑边）。这里在 L 通道上模糊后当作 mask 回填。
    """
    alpha = image.getchannel("A")
    # radius 与尺寸挂钩，保证不同尺寸输出下的过渡幅度观感一致
    radius = max(image.width, image.height) / 512.0
    feathered = alpha.filter(ImageFilter.GaussianBlur(radius=radius))

    # 羽化会给本该全透明的区域残留极弱 alpha，归零以免出现方形底影
    feathered = feathered.point(lambda v: 0 if v < 8 else v)

    result = image.copy()
    result.putalpha(feathered)
    return result


def build_base_image(source_path: Path) -> Image.Image:
    """母版 -> 裁边 -> 抠背景 -> 加圆角 -> 羽化，得到用于各尺寸降采样的透明底母图。

    顺序要点：先抠背景再切圆角。圆角遮罩只切掉四角，若先切圆角，
    四角变透明后 flood fill 会从这些新透明的角落向内扩散，
    误把白卡片边缘一起抠掉。
    """
    base = crop_to_content(load_source(source_path))
    base = remove_border_connected_background(base)
    base = apply_card_rounding(base)
    return feather_alpha(base)


def save_png(base: Image.Image, size: int, target: Path) -> None:
    """按指定边长从母图降采样并写出 PNG。"""
    base.resize((size, size), Image.LANCZOS).save(target, "PNG", optimize=True)
    print(f"  PNG  {target.name:<22} {size}x{size}")


def save_ico(base: Image.Image, sizes: tuple[int, ...], target: Path) -> None:
    """生成多层 ico，每层独立从母图降采样。

    这里必须直接把母图交给 Pillow 并声明 sizes：Pillow 会逐层执行
    `base.resize(size, LANCZOS)`，而不是先生成小图再放大。
    切勿传入 append_images —— 那样 Pillow 会忽略 sizes，产出只有一帧的 ico。
    """
    base.save(target, format="ICO", sizes=[(s, s) for s in sizes], bitmap_format="png")
    print(f"  ICO  {target.name:<22} {', '.join(str(s) for s in sizes)}")


def main() -> int:
    parser = argparse.ArgumentParser(description="生成 MarketAssistant 多分辨率应用图标")
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE, help="设计母版 PNG 路径")
    parser.add_argument("--assets", type=Path, default=DEFAULT_ASSETS, help="图标输出目录（App 的 Assets）")
    args = parser.parse_args()

    if not args.assets.is_dir():
        print(f"错误：输出目录不存在：{args.assets}", file=sys.stderr)
        return 1

    print(f"母版：{args.source}")
    base = build_base_image(args.source)
    print(f"基准图：{base.width}x{base.height}（已裁边、抠背景、羽化）\n")

    print("生成图标：")
    # 规范图标：供 exe / 窗口 / 安装包共用，多层覆盖所有缩放档位
    save_ico(base, ICO_SIZES, args.assets / "logo.ico")
    # 托盘专用：Windows 会挑最接近的一档，小尺寸档位够用即可
    save_ico(base, TRAY_ICO_SIZES, args.assets / "tray_logo.ico")
    # UI 内展示与各平台打包
    save_png(base, UI_PNG_SIZE, args.assets / "logo_ui.png")
    save_png(base, LINUX_PNG_SIZE, args.assets / "logo_256.png")
    save_png(base, MACOS_PNG_SIZE, args.assets / "logo_1024.png")

    print("\n完成。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
