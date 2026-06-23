#!/bin/bash
set -e

# MarketAssistant Linux .rpm 构建脚本
# 适用于 Fedora、RHEL、CentOS

APP_NAME="marketassistant"
VERSION="1.0.0"
RELEASE="1"
ARCH="x86_64"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BUILD_DIR="$PROJECT_ROOT/Release/Linux"
PUBLISH_DIR="$BUILD_DIR/publish"
RPM_DIR="$BUILD_DIR/rpm"

# 颜色输出
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${GREEN}🚀 Building $APP_NAME RPM package...${NC}"

# 检查 rpmbuild 是否安装
if ! command -v rpmbuild &> /dev/null; then
    echo -e "${RED}✗ rpmbuild not found. Install with: sudo dnf install rpm-build${NC}"
    exit 1
fi

# 确保应用已发布
if [ ! -d "$PUBLISH_DIR" ]; then
    echo -e "${RED}✗ Publish directory not found. Run Linux build first.${NC}"
    exit 1
fi

# 创建 RPM 构建目录结构
echo -e "${YELLOW}📂 Creating RPM build structure...${NC}"
mkdir -p "$RPM_DIR"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

# 创建 spec 文件
echo -e "${YELLOW}📝 Creating spec file...${NC}"
cat > "$RPM_DIR/SPECS/$APP_NAME.spec" << EOF
Name:           $APP_NAME
Version:        $VERSION
Release:        $RELEASE%{?dist}
Summary:        AI 智能市场分析助手
License:        Proprietary
URL:            https://github.com/yourusername/MarketAssistant
BuildArch:      $ARCH

Requires:       libicu >= 60, openssl-libs >= 1.1

%description
市场分析助手是一款跨平台的桌面应用程序，提供
AI 智能市场分析与洞察。它具有实时数据分析、
技术指标和智能推荐功能。

%prep
# Nothing to prep

%build
# Nothing to build

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}/opt/MarketAssistant
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons/hicolor/256x256/apps
mkdir -p %{buildroot}/usr/bin

# 复制应用文件
cp -r $PUBLISH_DIR/* %{buildroot}/opt/MarketAssistant/

# 复制桌面文件
cp $SCRIPT_DIR/marketassistant.desktop %{buildroot}/usr/share/applications/

# 复制图标
if [ -f $PROJECT_ROOT/src/Assets/logo.png ]; then
    cp $PROJECT_ROOT/src/Assets/logo.png %{buildroot}/usr/share/icons/hicolor/256x256/apps/marketassistant.png
fi

# 创建符号链接
ln -s /opt/MarketAssistant/MarketAssistant %{buildroot}/usr/bin/marketassistant

%files
/opt/MarketAssistant/*
/usr/share/applications/marketassistant.desktop
/usr/share/icons/hicolor/256x256/apps/marketassistant.png
/usr/bin/marketassistant

%post
# 更新桌面数据库
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database -q
fi

# 更新图标缓存
if command -v gtk-update-icon-cache &> /dev/null; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi

%postun
# 清理
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database -q
fi

%changelog
* $(date "+%a %b %d %Y") MarketAssistant Team <support@marketassistant.com> - $VERSION-$RELEASE
- Initial RPM release
EOF

# 构建 RPM
echo -e "${YELLOW}🔨 Building RPM package...${NC}"
rpmbuild --define "_topdir $RPM_DIR" -bb "$RPM_DIR/SPECS/$APP_NAME.spec"

if [ $? -eq 0 ]; then
    # 查找生成的 RPM
    RPM_FILE=$(find "$RPM_DIR/RPMS" -name "*.rpm" | head -1)
    if [ -n "$RPM_FILE" ]; then
        cp "$RPM_FILE" "$BUILD_DIR/MarketAssistant-$VERSION-$ARCH.rpm"
        RPM_SIZE=$(du -h "$BUILD_DIR/MarketAssistant-$VERSION-$ARCH.rpm" | cut -f1)
        echo -e "${GREEN}✓ RPM package created: $RPM_SIZE${NC}"
    fi
fi

# 输出摘要
echo ""
echo -e "${GREEN}✅ RPM build completed successfully!${NC}"
echo ""
echo "📦 Output:"
echo "   • RPM package: $BUILD_DIR/MarketAssistant-$VERSION-$ARCH.rpm"
echo ""
echo "🧪 To test RPM package:"
echo "   sudo rpm -ivh $BUILD_DIR/MarketAssistant-$VERSION-$ARCH.rpm"
echo ""
echo "🗑️ To uninstall:"
echo "   sudo rpm -e $APP_NAME"
echo ""

