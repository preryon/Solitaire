# Solitaire-Precor

A cross-platform Solitaire game application built with Avalonia UI, featuring custom PRECOR branding and enhanced user interface.

## Overview

This is a professional solitaire game application that supports multiple game variants including Klondike, Spider, and FreeCell. The application features a custom PRECOR logo on card backs and an enhanced button bar interface optimized for touch devices.

## Features

- **Multiple Game Variants**: Klondike, Spider, and FreeCell solitaire
- **Custom Branding**: PRECOR logo integrated into card backs
- **Enhanced UI**: Larger, touch-friendly button bar with centered text
- **Cross-Platform**: Runs on Windows, macOS, Linux, and Android
- **Optimized Performance**: AOT compilation for Android builds

## Technical Specifications

### Framework & Dependencies
- **Framework**: .NET 9.0
- **UI Framework**: Avalonia UI
- **MVVM**: CommunityToolkit.Mvvm 8.0.0
- **Interactivity**: Avalonia.Xaml.Interactivity 11.0.0
- **Target Platforms**: 
  - Desktop: Windows, macOS, Linux
  - Mobile: Android (ARM64)

### Project Structure
```
Solitaire/
├── Solitaire/                    # Core library (shared code)
├── Solitaire.Desktop/            # Desktop application entry point
├── Solitaire.Android/           # Android application
├── Assets/                       # Application assets
│   └── Images/                  # Image resources
│       └── precor_logo.png      # Custom PRECOR logo (3.7 MB)
└── Styles/                      # XAML styling files
    ├── CardsStyle.axaml         # Card styling and resources
    └── PlayingCard.axaml        # Individual card template
```

## Version Information

- **Current Version**: 1.2
- **Build Version**: 3
- **Application ID**: com.precor.Solitaire
- **Target Framework**: net9.0

## Prerequisites

### For Development
- .NET 9.0 SDK
- Visual Studio 2022 or VS Code with C# extension
- Git

### For Android Builds
- Android SDK (API Level 21+)
- Java Development Kit (JDK 11+)
- Android SDK Build Tools

## Building the Application

### Desktop Application

#### Run in Development Mode
```bash
dotnet run --project Solitaire.Desktop/Solitaire.Desktop.csproj -c Release
```

#### Build for Distribution
```bash
dotnet publish Solitaire.Desktop/Solitaire.Desktop.csproj -c Release
```

### Android Application (Standalone APK)

#### Prerequisites Setup
1. Set environment variables for Android SDK and Java:
```bash
export AndroidSdkDirectory=~/Android/
export JavaSdkDirectory=/home/ryon/jdk-11.0.28+6
```

2. Ensure Android SDK is properly installed and configured.

#### Build Optimized APK
```bash
dotnet publish Solitaire.Android/Solitaire.Android.csproj -c Release \
  -p:AndroidArchitectures=arm64 \
  -p:AndroidEnableProfiledAot=true \
  -p:AndroidCreatePackagePerAbi=true
```

#### Output Location
The signed APK will be generated at:
```
./Solitaire.Android/bin/Release/net9.0-android/publish/com.precor.Solitaire-arm64-v8a-Signed.apk
```

## APK Specifications

- **File Name**: `com.precor.Solitaire-arm64-v8a-Signed.apk`
- **Size**: ~31 MB
- **Architecture**: ARM64 (compatible with most modern Android devices)
- **Signing**: Automatically signed for distribution
- **Optimization**: AOT compilation enabled for better performance

## Custom Assets

### PRECOR Logo
- **Source File**: `Solitaire/Assets/Images/precor_logo.png` (3.7 MB)
- **Optimized Version**: `precor_logo_new_tiny.jpg` (17 KB)
- **Usage**: Displayed on card backs in all game variants
- **Format**: PNG (source), JPG (optimized for performance)

### Button Bar Enhancements
- **Height**: 180 pixels (50% taller than original)
- **Width**: 1575 pixels (125% wider than original)
- **Button Size**: 234 × 90 pixels each
- **Font Size**: 36pt for buttons, 36pt labels, 42pt values
- **Text Alignment**: Centered both horizontally and vertically

## Development Notes

### Key Files Modified
- `Solitaire/Styles/PlayingCard.axaml`: Card back image integration
- `Solitaire/Controls/DeckControls.axaml`: Enhanced button bar UI
- `Solitaire.Android/Solitaire.Android.csproj`: Version and build configuration

### Performance Optimizations
- Image compression: 3.7 MB PNG → 17 KB JPG
- ARM64-specific builds for optimal Android performance
- AOT compilation for faster startup times

## Troubleshooting

### Common Build Issues

1. **Android SDK Not Found**
   - Ensure environment variables are set correctly
   - Verify Android SDK installation path

2. **Java SDK Issues**
   - Confirm JDK 11+ is installed
   - Check JavaSdkDirectory environment variable

3. **XAML Compilation Errors**
   - Check for duplicate attributes in XAML files
   - Ensure proper namespace declarations

### Performance Issues
- Use ARM64-specific builds for Android
- Enable AOT compilation for production releases
- Optimize image assets for target platform

## License

This project is proprietary software developed for PRECOR. All rights reserved.

## Support

For technical support or questions regarding this application, please contact the development team.

---

**Last Updated**: September 18, 2024  
**Version**: 1.2  
**Build**: 3
