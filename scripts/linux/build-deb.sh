#!/bin/bash
set -e

# MarketAssistant Linux .deb 构建脚本
# 遵循 Debian 软件包规范

APP_NAME="marketassistant"
VERSION="1.0.0"
ARCH="amd64"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="$PROJECT_ROOT/Release/Linux"
PUBLISH_DIR="$BUILD_DIR/publish"
DEB_DIR="$BUILD_DIR/deb"
PACKAGE_NAME="${APP_NAME}_${VERSION}_${ARCH}"

# 颜色输出
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${GREEN}🚀 Building $APP_NAME for Linux...${NC}"

# 清理旧构建
if [ -d "$BUILD_DIR" ]; then
    echo -e "${YELLOW}📦 Cleaning old build...${NC}"
    rm -rf "$BUILD_DIR"
fi

mkdir -p "$BUILD_DIR"

# 1. 发布应用
echo -e "${YELLOW}📦 Publishing app...${NC}"
cd "$PROJECT_ROOT"

dotnet publish src/MarketAssistant.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained \
    -p:PublishSingleFile=false \
    -p:PublishReadyToRun=true \
    -p:UseAppHost=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -p:ErrorOnDuplicatePublishOutputFiles=false \
    -o "$PUBLISH_DIR"

if [ $? -ne 0 ]; then
    echo -e "${RED}✗ Build failed${NC}"
    exit 1
fi

# 2. 创建 .deb 包结构
echo -e "${YELLOW}📂 Creating .deb structure...${NC}"
mkdir -p "$DEB_DIR/$PACKAGE_NAME"
mkdir -p "$DEB_DIR/$PACKAGE_NAME/DEBIAN"
mkdir -p "$DEB_DIR/$PACKAGE_NAME/opt/MarketAssistant"
mkdir -p "$DEB_DIR/$PACKAGE_NAME/usr/share/applications"
mkdir -p "$DEB_DIR/$PACKAGE_NAME/usr/share/icons/hicolor/256x256/apps"
mkdir -p "$DEB_DIR/$PACKAGE_NAME/usr/bin"

# 3. 复制应用文件
echo -e "${YELLOW}📋 Copying application files...${NC}"
cp -r "$PUBLISH_DIR/"* "$DEB_DIR/$PACKAGE_NAME/opt/MarketAssistant/"

# 确保可执行文件有执行权限
chmod +x "$DEB_DIR/$PACKAGE_NAME/opt/MarketAssistant/MarketAssistant"

# 4. 创建符号链接到 /usr/bin
echo -e "${YELLOW}🔗 Creating symbolic link...${NC}"
cd "$DEB_DIR/$PACKAGE_NAME/usr/bin"
ln -s "/opt/MarketAssistant/MarketAssistant" "marketassistant"
cd "$SCRIPT_DIR"

# 5. 复制桌面文件
echo -e "${YELLOW}📝 Installing desktop entry...${NC}"
cp "$SCRIPT_DIR/marketassistant.desktop" "$DEB_DIR/$PACKAGE_NAME/usr/share/applications/"

# 6. 复制图标
if [ -f "$PROJECT_ROOT/src/Assets/logo.png" ]; then
    echo -e "${YELLOW}🎨 Installing icon...${NC}"
    cp "$PROJECT_ROOT/src/Assets/logo.png" "$DEB_DIR/$PACKAGE_NAME/usr/share/icons/hicolor/256x256/apps/marketassistant.png"
fi

# 7. 创建 control 文件
echo -e "${YELLOW}📄 Creating control file...${NC}"
INSTALLED_SIZE=$(du -s "$DEB_DIR/$PACKAGE_NAME" | cut -f1)

cat > "$DEB_DIR/$PACKAGE_NAME/DEBIAN/control" << EOF
Package: $APP_NAME
Version: $VERSION
Section: misc
Priority: optional
Architecture: $ARCH
Installed-Size: $INSTALLED_SIZE
Maintainer: MarketAssistant Team <support@marketassistant.com>
Homepage: https://github.com/yourusername/MarketAssistant
Description: AI-powered market analysis assistant
 Market Assistant is a cross-platform desktop application that provides
 AI-powered market analysis and insights. It features real-time data
 analysis, technical indicators, and intelligent recommendations.
Depends: libicu70 | libicu72, libssl3 | libssl1.1
EOF

# 8. 创建 postinst 脚本（安装后）
cat > "$DEB_DIR/$PACKAGE_NAME/DEBIAN/postinst" << 'EOF'
#!/bin/bash
set -e

# 更新桌面数据库
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database -q
fi

# 更新图标缓存
if command -v gtk-update-icon-cache &> /dev/null; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi

exit 0
EOF

chmod 755 "$DEB_DIR/$PACKAGE_NAME/DEBIAN/postinst"

# 9. 创建 prerm 脚本（卸载前）
cat > "$DEB_DIR/$PACKAGE_NAME/DEBIAN/prerm" << 'EOF'
#!/bin/bash
set -e
exit 0
EOF

chmod 755 "$DEB_DIR/$PACKAGE_NAME/DEBIAN/prerm"

# 10. 构建 .deb 包
echo -e "${YELLOW}🔨 Building .deb package...${NC}"
cd "$DEB_DIR"
dpkg-deb --build --root-owner-group "$PACKAGE_NAME"

if [ $? -eq 0 ]; then
    DEB_SIZE=$(du -h "$DEB_DIR/$PACKAGE_NAME.deb" | cut -f1)
    echo -e "${GREEN}✓ .deb package created: $DEB_SIZE${NC}"
    
    # 移动到最终位置
    mv "$PACKAGE_NAME.deb" "$BUILD_DIR/MarketAssistant-$VERSION-$ARCH.deb"
fi

# 11. 验证包
echo -e "${YELLOW}🔍 Verifying package...${NC}"
dpkg-deb --info "$BUILD_DIR/MarketAssistant-$VERSION-$ARCH.deb"

# 12. 创建 ZIP 归档（兼容性）
echo -e "${YELLOW}📦 Creating ZIP archive...${NC}"
cd "$PUBLISH_DIR"
zip -r "$BUILD_DIR/MarketAssistant-Linux-x64.zip" . -q

ZIP_SIZE=$(du -h "$BUILD_DIR/MarketAssistant-Linux-x64.zip" | cut -f1)
echo -e "${GREEN}✓ ZIP archive created: $ZIP_SIZE${NC}"

# 输出摘要
echo ""
echo -e "${GREEN}✅ Build completed successfully!${NC}"
echo ""
echo "📦 Output:"
echo "   • .deb package: $BUILD_DIR/MarketAssistant-$VERSION-$ARCH.deb"
echo "   • ZIP archive: $BUILD_DIR/MarketAssistant-Linux-x64.zip"
echo ""
echo "🧪 To test .deb package:"
echo "   sudo dpkg -i $BUILD_DIR/MarketAssistant-$VERSION-$ARCH.deb"
echo ""
echo "🗑️ To uninstall:"
echo "   sudo apt remove $APP_NAME"
echo ""

