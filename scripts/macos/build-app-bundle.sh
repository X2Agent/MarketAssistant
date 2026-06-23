#!/bin/bash
set -e

# MarketAssistant macOS App Bundle 构建脚本
# 遵循 Avalonia 官方 macOS 部署规范

APP_NAME="MarketAssistant"
# 优先使用 CI 注入的版本号，否则回退到默认值
VERSION="${APP_VERSION:-1.0.0}"
BUNDLE_ID="xyz.haoai.market"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
APP_CSProj="$PROJECT_ROOT/src/MarketAssistant.App/MarketAssistant.App.csproj"
APP_ASSETS_DIR="$PROJECT_ROOT/src/MarketAssistant.App/Assets"
BUILD_DIR="$PROJECT_ROOT/Release/macOS"
PUBLISH_DIR="$BUILD_DIR/publish"
APP_BUNDLE="$BUILD_DIR/$APP_NAME.app"

# 颜色输出
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${GREEN}🚀 Building $APP_NAME for macOS...${NC}"

# 清理旧构建
if [ -d "$BUILD_DIR" ]; then
    echo -e "${YELLOW}📦 Cleaning old build...${NC}"
    rm -rf "$BUILD_DIR"
fi

mkdir -p "$BUILD_DIR"

# 1. 发布应用
echo -e "${YELLOW}📦 Publishing app...${NC}"
cd "$PROJECT_ROOT"

dotnet publish "$APP_CSProj" \
    -c Release \
    -r osx-x64 \
    --self-contained \
    -p:PublishSingleFile=false \
    -p:PublishReadyToRun=true \
    -p:UseAppHost=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -p:ErrorOnDuplicatePublishOutputFiles=false \
    -p:Version="$VERSION" \
    -p:InformationalVersion="$VERSION" \
    -o "$PUBLISH_DIR"

if [ $? -ne 0 ]; then
    echo -e "${RED}✗ Build failed${NC}"
    exit 1
fi

# 2. 创建 .app bundle 结构
echo -e "${YELLOW}📂 Creating .app bundle structure...${NC}"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources"

# 3. 复制二进制文件
echo -e "${YELLOW}📋 Copying binaries...${NC}"
cp -r "$PUBLISH_DIR/"* "$APP_BUNDLE/Contents/MacOS/"

# 确保可执行文件有执行权限
chmod +x "$APP_BUNDLE/Contents/MacOS/$APP_NAME"

# 4. 创建 Info.plist
echo -e "${YELLOW}📝 Creating Info.plist...${NC}"
# 基于 tag 版本号注入到 Info.plist
sed -e "s|__APP_VERSION__|$VERSION|g" \
    -e "s|__BUNDLE_ID__|$BUNDLE_ID|g" \
    "$SCRIPT_DIR/Info.plist.template" > "$APP_BUNDLE/Contents/Info.plist"

# 5. 复制图标（如果存在）
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

# 6. 代码签名（如果有证书）
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

# 7. 创建 DMG
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

# 8. 公证（如果配置了）
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

# 9. 验证
echo -e "${YELLOW}🔍 Verifying bundle...${NC}"
if [ -n "$SIGNING_IDENTITY" ]; then
    codesign --verify --deep --strict --verbose=2 "$APP_BUNDLE" 2>&1 | head -5
fi

# 输出摘要
echo ""
echo -e "${GREEN}✅ Build completed successfully!${NC}"
echo ""
echo "📦 Output:"
echo "   • App Bundle: $APP_BUNDLE"
echo "   • DMG: $DMG_PATH"
echo ""
echo "🧪 To test locally:"
echo "   open \"$APP_BUNDLE\""
echo ""

