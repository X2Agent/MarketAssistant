#!/bin/bash
set -e

# MarketAssistant macOS App Bundle 构建脚本
# 遵循 Avalonia 官方 macOS 部署规范
# 构建 Universal Binary（同时支持 Apple Silicon arm64 和 Intel x64）

APP_NAME="MarketAssistant"
# 优先使用 CI 注入的版本号，否则回退到默认值
VERSION="${APP_VERSION:-1.0.0}"
BUNDLE_ID="xyz.haoai.market"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
APP_CSProj="$PROJECT_ROOT/src/MarketAssistant.App/MarketAssistant.App.csproj"
APP_ASSETS_DIR="$PROJECT_ROOT/src/MarketAssistant.App/Assets"
BUILD_DIR="$PROJECT_ROOT/Release/macOS"
PUBLISH_DIR_ARM64="$BUILD_DIR/publish-arm64"
PUBLISH_DIR_X64="$BUILD_DIR/publish-x64"
APP_BUNDLE="$BUILD_DIR/$APP_NAME.app"

# 颜色输出
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${GREEN}🚀 Building $APP_NAME for macOS (Universal Binary)...${NC}"

# 清理旧构建
if [ -d "$BUILD_DIR" ]; then
    echo -e "${YELLOW}📦 Cleaning old build...${NC}"
    rm -rf "$BUILD_DIR"
fi

mkdir -p "$BUILD_DIR"

# 通用 publish 参数
PUBLISH_ARGS=(
    "$APP_CSProj"
    -c Release
    --self-contained
    -p:PublishSingleFile=false
    -p:PublishReadyToRun=true
    -p:UseAppHost=true
    -p:DebugType=None
    -p:DebugSymbols=false
    -p:ErrorOnDuplicatePublishOutputFiles=false
    -p:Version="$VERSION"
    -p:InformationalVersion="$VERSION"
)

# 1. 发布 arm64 架构
echo -e "${YELLOW}📦 Publishing arm64 (Apple Silicon)...${NC}"
cd "$PROJECT_ROOT"
dotnet publish "${PUBLISH_ARGS[@]}" -r osx-arm64 -o "$PUBLISH_DIR_ARM64"
if [ $? -ne 0 ]; then
    echo -e "${RED}✗ arm64 build failed${NC}"
    exit 1
fi

# 2. 发布 x64 架构
echo -e "${YELLOW}📦 Publishing x64 (Intel)...${NC}"
dotnet publish "${PUBLISH_ARGS[@]}" -r osx-x64 -o "$PUBLISH_DIR_X64"
if [ $? -ne 0 ]; then
    echo -e "${RED}✗ x64 build failed${NC}"
    exit 1
fi

# 3. 创建 .app bundle 结构
echo -e "${YELLOW}📂 Creating .app bundle structure...${NC}"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# 4. 复制 arm64 的所有文件作为基础（managed 代码与架构无关）
echo -e "${YELLOW}📋 Copying base files (arm64)...${NC}"
cp -r "$PUBLISH_DIR_ARM64/"* "$APP_BUNDLE/Contents/MacOS/"

# 5. 用 lipo 合并主可执行文件为 Universal Binary
echo -e "${YELLOW}🔗 Creating Universal Binary for main executable...${NC}"
lipo -create \
    "$PUBLISH_DIR_ARM64/$APP_NAME" \
    "$PUBLISH_DIR_X64/$APP_NAME" \
    -output "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

if [ $? -ne 0 ]; then
    echo -e "${RED}✗ lipo merge failed for main executable${NC}"
    exit 1
fi

# 确保可执行文件有执行权限
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

# 6. 遍历合并所有 .dylib 文件（native 库与架构相关，需合并）
echo -e "${YELLOW}🔗 Merging native libraries (.dylib) into Universal...${NC}"
DYLIB_COUNT=0
while IFS= read -r -d '' dylib_arm64; do
    # 计算相对路径
    rel_path="${dylib_arm64#$PUBLISH_DIR_ARM64/}"
    dylib_x64="$PUBLISH_DIR_X64/$rel_path"

    if [ -f "$dylib_x64" ]; then
        # 两个架构都存在，合并为 Universal（失败时保留 arm64 版本，不中断构建）
        if lipo -create "$dylib_arm64" "$dylib_x64" \
            -output "$APP_BUNDLE/Contents/MacOS/$rel_path" 2>/dev/null; then
            DYLIB_COUNT=$((DYLIB_COUNT + 1))
        else
            echo -e "${YELLOW}   ⚠ lipo failed for $rel_path, keeping arm64${NC}"
        fi
    fi
    # 若 x64 不存在该 dylib，arm64 版本已通过 cp 复制，无需处理
done < <(find "$PUBLISH_DIR_ARM64" -type f -name "*.dylib" -print0)

echo -e "${GREEN}✓ Merged $DYLIB_COUNT native libraries${NC}"

# 7. 验证 Universal Binary
echo -e "${YELLOW}🔍 Verifying Universal Binary...${NC}"
ARCH_INFO=$(lipo -archs "$APP_BUNDLE/Contents/MacOS/$APP_NAME")
echo -e "${GREEN}✓ Main executable architectures: $ARCH_INFO${NC}"
if [[ "$ARCH_INFO" != *"arm64"* ]] || [[ "$ARCH_INFO" != *"x86_64"* ]]; then
    echo -e "${RED}✗ Universal Binary verification failed${NC}"
    exit 1
fi

# 8. 创建 Info.plist
echo -e "${YELLOW}📝 Creating Info.plist...${NC}"
# 基于 tag 版本号注入到 Info.plist
sed -e "s|__APP_VERSION__|$VERSION|g" \
    -e "s|__BUNDLE_ID__|$BUNDLE_ID|g" \
    "$SCRIPT_DIR/Info.plist.template" > "$APP_BUNDLE/Contents/Info.plist"

# 9. 复制图标（如果存在）
if [ -f "$APP_ASSETS_DIR/MarketAssistant.icns" ]; then
    echo -e "${YELLOW}🎨 Copying icon...${NC}"
    cp "$APP_ASSETS_DIR/MarketAssistant.icns" "$APP_BUNDLE/Contents/Resources/"
elif [ -f "$APP_ASSETS_DIR/logo.png" ]; then
    echo -e "${YELLOW}🎨 Converting PNG to ICNS...${NC}"
    # 如果只有 PNG，尝试转换（需要 imagemagick 或 sips）
    if command -v sips &> /dev/null; then
        mkdir -p /tmp/iconset.iconset
        sips -z 16 16 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_16x16.png
        sips -z 32 32 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_16x16@2x.png
        sips -z 32 32 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_32x32.png
        sips -z 64 64 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_32x32@2x.png
        sips -z 128 128 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_128x128.png
        sips -z 256 256 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_128x128@2x.png
        sips -z 256 256 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_256x256.png
        sips -z 512 512 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_256x256@2x.png
        sips -z 512 512 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_512x512.png
        sips -z 1024 1024 "$APP_ASSETS_DIR/logo.png" --out /tmp/iconset.iconset/icon_512x512@2x.png
        iconutil -c icns /tmp/iconset.iconset -o "$APP_BUNDLE/Contents/Resources/MarketAssistant.icns"
        rm -rf /tmp/iconset.iconset
    fi
fi

# 10. 代码签名（如果有证书）
if [ -n "$SIGNING_IDENTITY" ]; then
    echo -e "${YELLOW}🔐 Code signing...${NC}"

    # 签名所有可执行文件和库
    find "$APP_BUNDLE/Contents/MacOS" -type f \( -name "*.dylib" -o -perm +111 \) | while read file; do
        codesign --force --timestamp --options=runtime \
            --entitlements "$SCRIPT_DIR/MarketAssistant.entitlements" \
            --sign "$SIGNING_IDENTITY" \
            "$file" 2>/dev/null || true
    done

    # 签名整个 bundle
    codesign --force --timestamp --options=runtime \
        --entitlements "$SCRIPT_DIR/MarketAssistant.entitlements" \
        --sign "$SIGNING_IDENTITY" \
        "$APP_BUNDLE"

    echo -e "${GREEN}✓ Code signing completed${NC}"
else
    echo -e "${YELLOW}⚠ Skipping code signing (no SIGNING_IDENTITY set)${NC}"
fi

# 11. 创建 DMG
echo -e "${YELLOW}💿 Creating DMG...${NC}"
DMG_PATH="$BUILD_DIR/$APP_NAME-$VERSION.dmg"

if [ -f "$DMG_PATH" ]; then
    rm "$DMG_PATH"
fi

hdiutil create -volname "$APP_NAME" \
    -srcfolder "$APP_BUNDLE" \
    -ov -format UDZO \
    "$DMG_PATH"

if [ $? -eq 0 ]; then
    DMG_SIZE=$(du -h "$DMG_PATH" | cut -f1)
    echo -e "${GREEN}✓ DMG created: $DMG_SIZE${NC}"
fi

# 12. 公证（如果配置了）
if [ -n "$NOTARYTOOL_PROFILE" ]; then
    echo -e "${YELLOW}📜 Notarizing app...${NC}"

    # 提交公证
    xcrun notarytool submit "$DMG_PATH" \
        --keychain-profile "$NOTARYTOOL_PROFILE" \
        --wait

    if [ $? -eq 0 ]; then
        # 附加公证票据
        xcrun stapler staple "$APP_BUNDLE"
        xcrun stapler staple "$DMG_PATH"
        echo -e "${GREEN}✓ Notarization completed${NC}"
    else
        echo -e "${RED}✗ Notarization failed${NC}"
    fi
else
    echo -e "${YELLOW}⚠ Skipping notarization (no NOTARYTOOL_PROFILE set)${NC}"
fi

# 13. 验证
echo -e "${YELLOW}🔍 Verifying bundle...${NC}"
if [ -n "$SIGNING_IDENTITY" ]; then
    codesign --verify --deep --strict --verbose=2 "$APP_BUNDLE" 2>&1 | head -5
fi

# 输出摘要
echo ""
echo -e "${GREEN}✅ Build completed successfully!${NC}"
echo ""
echo "📦 Output:"
echo "   • App Bundle: $APP_BUNDLE (Universal: arm64 + x86_64)"
echo "   • DMG: $DMG_PATH"
echo ""
echo "🧪 To test locally:"
echo "   open \"$APP_BUNDLE\""
echo ""
