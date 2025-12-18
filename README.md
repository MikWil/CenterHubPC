# CenterHub

A modern Windows desktop application built with WPF and .NET 8, providing system monitoring, audio controls, file management, and productivity tools.

## Features

- **System Monitoring**: Real-time CPU, GPU, memory, and disk usage monitoring
- **Audio Controls**: Manage audio devices and application volumes
- **File Management**: Move or copy files between directories
- **Productivity Tools**: Standing/sitting timer with notifications
- **System Controls**: Schedule computer shutdown

## Architecture

### MVVM Pattern
The application follows the Model-View-ViewModel (MVVM) pattern with the following structure:

```
MVVM/
├── Models/          # Data models and entities
├── View/           # WPF Views (XAML)
├── ViewModel/      # ViewModels with business logic
├── Services/       # Business services and data access
├── Configuration/  # Configuration management
└── Navigation/     # Navigation interfaces
```

### Dependency Injection
The application uses Microsoft.Extensions.DependencyInjection for proper dependency management:

- **Services**: Singleton services for system monitoring, caching, and configuration
- **ViewModels**: Transient ViewModels for proper lifecycle management
- **Views**: Transient Views with dependency injection

### Key Components

#### Base Classes
- `BaseViewModel`: Common ViewModel functionality with logging and disposal
- `ISystemMonitorService`: Interface for system monitoring
- `ICacheService`: Interface for caching operations
- `IConfigurationService`: Interface for configuration management

#### Services
- `SystemMonitorService`: Monitors CPU, GPU, memory, and disk usage
- `CacheService`: In-memory caching with expiration support
- `ConfigurationService`: JSON-based configuration management

#### ViewModels
- `MainViewModel`: Main application navigation and view management
- `HomeViewModel`: System monitoring dashboard
- `SoundViewModel`: Audio device management
- `SoundControlsViewModel`: Advanced audio controls
- `StandingViewModel`: Standing/sitting timer
- `MoveFilesViewModel`: File operations
- `ComputerViewModel`: System controls

## Technology Stack

- **.NET 8**: Latest .NET framework
- **WPF**: Windows Presentation Foundation for UI
- **CommunityToolkit.Mvvm**: Modern MVVM toolkit
- **MaterialDesignThemes**: Material Design UI components
- **LibreHardwareMonitor**: Hardware monitoring
- **NAudio**: Audio device management
- **Microsoft.Extensions**: Dependency injection and logging

## Getting Started

### Prerequisites
- Windows 10/11
- .NET 8 Runtime
- Visual Studio 2022 or later (for development)

### Installation
1. Download the latest release
2. Run the installer or extract the portable version
3. Launch `CenterHubNew.exe`

### Development Setup
1. Clone the repository
2. Open `CenterHubNew.sln` in Visual Studio
3. Restore NuGet packages
4. Build and run the application

## Configuration

The application uses `appsettings.json` for configuration:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Application": {
    "StartMinimized": false,
    "ShowInSystemTray": true
  },
  "SystemMonitor": {
    "UpdateInterval": 2000,
    "EnableCpuMonitoring": true,
    "EnableGpuMonitoring": true
  }
}
```

## Logging

The application uses structured logging with Microsoft.Extensions.Logging:

- Console and Debug output for development
- Structured logging with semantic information
- Error tracking and performance monitoring

## Best Practices Implemented

### Code Quality
- **SOLID Principles**: Proper separation of concerns
- **Dependency Injection**: Loose coupling and testability
- **Interface Segregation**: Clean service contracts
- **Error Handling**: Comprehensive exception handling
- **Logging**: Structured logging throughout the application

### Performance
- **Lazy Loading**: ViewModels created on demand
- **Caching**: In-memory caching for frequently accessed data
- **Async Operations**: Non-blocking UI operations
- **Resource Management**: Proper disposal patterns

### User Experience
- **System Tray Integration**: Minimize to system tray
- **Notifications**: Windows toast notifications
- **Modern UI**: Material Design components
- **Responsive Design**: Adaptive layouts

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Version History

### v2.2.5
- Implemented dependency injection
- Added comprehensive logging
- Improved error handling
- Enhanced code architecture
- Added configuration management
- Updated to .NET 8
- Implemented proper disposal patterns
