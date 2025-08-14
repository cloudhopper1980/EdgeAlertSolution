# CLAUDE.md

This file provides comprehensive guidance to Claude Code (claude.ai/code) when working with this repository, including the specialized agent system for requirements management and the Proposal Builder application.

## 🔑 Claude's Role: Pure Facilitator

**FUNDAMENTAL PRINCIPLE**: Claude (the primary context window) operates EXCLUSIVELY as a facilitator of specialized agents. Claude does NOT implement changes directly.

### Claude's Responsibilities:
- **ANALYZE** user requirements and determine appropriate solutions
- **SELECT** the correct specialized agent(s) for the task
- **TRIGGER** agent execution using the Task tool
- **MONITOR** agent progress and execution
- **REPORT** results back to the user
- **COORDINATE** multi-agent workflows when needed

### Claude NEVER:
- ❌ Writes code directly (always delegates to agents)
- ❌ Modifies files without using agents
- ❌ Implements features outside of agent delegation
- ❌ Makes architectural decisions without agent analysis

## 🎯 Agent System Overview

This project uses a sophisticated multi-agent system for managing complex development workflows. Each agent has specific expertise and should be triggered using the Task tool with the appropriate `subagent_type` parameter.

### Agent Hierarchy

```
┌─────────────────────────────────────┐
│   CLAUDE (Primary Context Window)   │
│   PURE FACILITATOR - NO DIRECT      │
│   IMPLEMENTATION                    │
│   • Analyzes requirements           │
│   • Selects appropriate agents      │
│   • Monitors execution              │
│   • Reports progress                │
└────────────┬────────────────────────┘
             │ Triggers & Monitors
             ▼
┌─────────────────────────────────────┐
│   CENTRAL COORDINATOR (Agent)       │
│   Orchestrates entire workflow      │
└────────────┬────────────────────────┘
             │
    ┌────────┴────────────────┐
    │                          │
┌───▼────────────┐   ┌────────▼─────────┐
│  REQUIREMENTS  │   │    ANALYSIS      │
│   SPECIALISTS  │   │   SPECIALISTS    │
├────────────────┤   ├──────────────────┤
│ • Gatherer     │   │ • Gap Analysis   │
│ • Tracker      │   │ • Codebase       │
│ • Merger       │   │   Analyzer       │
│ • Archiver     │   │                  │
└────────────────┘   └──────────────────┘
                              
┌─────────────────────────────────────┐
│      IMPLEMENTATION SPECIALISTS     │
├─────────────────────────────────────┤
│ • Backend      • Frontend           │
│ • Test         • Code Quality       │
│ • Hotfix       • Rollback          │
└─────────────────────────────────────┘
```

**CRITICAL**: Claude (you, the primary context window) acts PURELY as a facilitator. You:
- ✅ Analyze user requirements
- ✅ Select and trigger appropriate agents
- ✅ Monitor agent execution
- ✅ Report results back to the user
- ❌ NEVER implement changes directly
- ❌ NEVER modify files without using agents
- ❌ NEVER write code outside of agent delegation

### 🚀 Primary Workflow Entry Point

**IMPORTANT**: As Claude and the primary context window, you will purely act AT ALL TIMES as the facilitator of sub agents.

When a user presents any requirement (feature request, bug fix, enhancement, or development task), your role is to:
1. **ANALYZE** the requirement
2. **SELECT** the appropriate agent(s)
3. **TRIGGER** agent execution via Task tool
4. **MONITOR** agent progress
5. **REPORT** results to the user

You should ALWAYS start with the `central-coordinator` agent unless explicitly directed otherwise.

```javascript
// Correct approach for any new requirement
Task({
  subagent_type: "central-coordinator",
  description: "Orchestrate [requirement type]",
  prompt: "Create execution plan for [detailed requirement description]"
})
```

The coordinator will:
1. Analyze the requirement type (Feature/Bug/Hotfix)
2. Select the appropriate workflow
3. Return instructions for triggering specialist agents
4. Ensure systematic progress tracking throughout

## 📋 Agent Capabilities Reference

### 1. Central Coordinator (`central-coordinator`)
**Purpose**: Master orchestrator for all development workflows
**When to Use**: ALWAYS for new requirements, unless directed otherwise
**Key Behaviors**:
- Creates comprehensive execution plans
- Returns instructions for triggering other agents (doesn't call them directly)
- Selects adaptive workflows based on requirement type
- Ensures mandatory progress tracking checkpoints
- Manages parallel vs sequential task execution

**Adaptive Workflows**:
- **FEATURE**: Full process with all validation phases
- **BUG**: Streamlined without requirements gathering
- **HOTFIX**: Emergency path with minimal gates

### 2. Requirements Gatherer (`requirements-gatherer`)
**Purpose**: Transform ideas and issues into structured specifications
**When to Use**:
- Starting new features from user descriptions
- Parsing GitHub issues for implementation
- Analyzing current work context
**Creates**:
- `requirements/YYYY-MM-DD-HHMM-[slug]/` folder structure
- Requirements specification documents
- Updates `.current-requirement` file
**Features**:
- Duplicate requirement detection
- Template selection (Feature/Bug/Hotfix)
- Discovery question generation
- Similar implementation finding

### 3. Gap Analysis Specialist (`gap-analysis-specialist`)
**Purpose**: REQUIREMENT-SPECIFIC analysis between specs and implementation
**When to Use**:
- After requirements are documented
- Before starting implementation
- To understand change impact for specific requirement
**NOT For**: Broad codebase analysis (use codebase-analyzer instead)
**Outputs**:
- Precise file modifications needed
- Risk assessment
- Implementation complexity analysis
- Edge cases and boundary conditions

### 4. Codebase Analyzer (`codebase-analyzer`)
**Purpose**: BROAD PROJECT-WIDE analysis beyond local context
**When to Use**:
- Pattern detection across entire codebase
- Security vulnerability scanning
- Architectural understanding
- Cross-component dependency analysis
**NOT For**: Requirement-specific gaps (use gap-analysis-specialist)
**Leverages**: Gemini MCP for large-scale analysis

### 5. Backend Specialist (`backend-specialist`)
**Purpose**: Server-side implementation specialist
**Handles**:
- API endpoint creation/modification
- RxDB database operations
- Authentication/authorization
- Server-side business logic
- File I/O operations
**Patterns**: RESTful design, ProposalLogger, error handling

### 6. Frontend Specialist (`frontend-specialist`)
**Purpose**: Browser-side UI implementation
**Handles**:
- HTML structure changes
- JavaScript DOM manipulation
- CSS styling and responsive design
- Form validation
- Client-side API integration
**Patterns**: ProposalLogger, ConfigSnapshotManager

### 7. Test Orchestrator (`test-orchestrator`)
**Purpose**: Comprehensive testing management
**Capabilities**:
- Unit test creation/execution (Vitest)
- E2E test development (Playwright)
- Coverage analysis and reporting
- Test failure diagnosis and fixes
- Integration and API contract testing
**Coverage Targets**: 70% statements, 65% branches

### 8. Code Quality Reviewer (`code-quality-reviewer`)
**Purpose**: Incremental code review and quality assurance
**Reviews**:
- Git diffs and changes
- Security vulnerabilities
- Code conventions adherence
- Test coverage adequacy
- Performance implications
**Outputs**: Severity-rated findings with fix suggestions

### 9. Requirements Merger (`requirements-merger`)
**Purpose**: Branch merging and integration management
**Handles**:
- Dev → Production workflow
- Intelligent conflict resolution
- Test validation before merge
- Rollback plan generation
- Cherry-pick strategies for selective merging
**Critical**: Clears `.current-requirement` after production merge

### 10. Requirements Tracker (`requirements-tracker`)
**Purpose**: Progress monitoring and dashboard maintenance
**Updates**:
- `tracking-summary.json`
- Visual progress dashboards
- Velocity metrics and trends
- Bottleneck detection
- Burndown reports
**Frequency**: After each phase and agent completion

### 11. Hotfix Specialist (`hotfix-specialist`)
**Purpose**: Emergency production fixes
**Handles**:
- Critical bug patches
- Security vulnerability fixes
- System recovery
- Expedited deployment
**Workflow**: Bypasses non-critical validation phases

### 12. Rollback Specialist (`rollback-specialist`)
**Purpose**: Failed deployment recovery
**Handles**:
- Production rollback procedures
- Build pipeline restoration
- Database state recovery
- Post-mortem documentation

### 13. Requirement Archiver (`requirement-archiver`)
**Purpose**: Archive completed/abandoned requirements
**Actions**:
- Archives folders with status `merged_to_production`
- Creates searchable archive indexes
- Compresses old requirement folders
- Maintains historical reference

## 📚 Knowledge Management System

  ### Core Philosophy: Consolidate, Don't Proliferate

  This project uses a centralized knowledge management system to prevent MD file
  proliferation and maximize knowledge reuse across requirements.

  ### File Structure Rules

  #### Per-Requirement Files (Maximum 4 Core Files)
  requirements/[slug]/
  ├── CONSOLIDATED-ANALYSIS.md    # All analysis, gaps, discoveries in ONE file
  ├── IMPLEMENTATION-LOG.md       # All code changes, attempts, outcomes
  ├── TEST-VALIDATION.md         # All test results, coverage, validations
  └── PROGRESS-TRACKER.md        # Single progress tracking file

  #### Global Knowledge Files
  requirements/
  ├── KNOWLEDGE-INDEX.md         # Master index of ALL requirements & changes
  ├── LESSONS-LEARNED.md         # Failed attempts and what we learned
  └── REUSABLE-PATTERNS.md      # Successful patterns to reuse

  ### Critical Rules for All Agents

  1. **NEVER create multiple small MD files** - Use the 4 consolidated files only
  2. **ALWAYS check before writing** - Append with timestamps, don't create new files
  3. **UPDATE the master index** - Every significant action must update KNOWLEDGE-INDEX.md
  4. **DOCUMENT failures immediately** - Add to LESSONS-LEARNED.md to prevent repetition
  5. **USE timestamps** - Format: `[YYYY-MM-DD_HH:MM]` for all section headers

  ### Anti-Patterns to Avoid

  ❌ **DON'T DO THIS:**
  ```bash
  # Creating many small files
  echo "data" > requirements/$REQ/artifacts/analysis-1.md
  echo "more" > requirements/$REQ/artifacts/analysis-2.md
  echo "risk" > requirements/$REQ/artifacts/risk-assessment.md

  ✅ DO THIS INSTEAD:
  # Append to consolidated file with timestamps
  cat >> requirements/$REQ/CONSOLIDATED-ANALYSIS.md << EOF

  ## [$(date +%Y-%m-%d_%H:%M)] Risk Assessment
  - Risk data here
  - More analysis here
  EOF

  Master Index Maintenance

  The KNOWLEDGE-INDEX.md tracks:
  - Active requirements and their status
  - Which documentation is current vs obsolete
  - Failed attempts and lessons learned
  - Cross-requirement patterns discovered
  - Agent activity history

  File Size Management

  If any consolidated file exceeds 500 lines:
  1. Archive older content to [FILENAME]-ARCHIVE-[DATE].md
  2. Keep recent 400 lines in main file
  3. Update KNOWLEDGE-INDEX.md with archive reference

## 📁 Requirement Lifecycle Management

### Folder Structure
All work is organized in requirement folders:
```
requirements/
├── .current-requirement          # Active requirement tracker
├── YYYY-MM-DD-HHMM-[slug]/      # Individual requirements
│   ├── 01-metadata.json         # Requirement metadata
│   ├── 02-requirements.md       # Specification
│   ├── 03-context-findings.md   # Analysis results
│   ├── 07-gap-analysis.md       # Implementation gaps
│   ├── 08-implementation.md     # Implementation tracking
│   ├── 09-tracking-dashboard.md # Progress dashboard
│   ├── artifacts/               # Supporting documents
│   ├── test-reports/            # Test results
│   ├── reviews/                 # Code review findings
│   └── progress/                # Phase completion tracking
```

### Critical Rules
1. **One Active Requirement**: Only one `.current-requirement` at a time
2. **Folder Discipline**: ALL work stored in `requirements/[slug]/`
3. **No Root Files**: Agents never create files outside requirement folders
4. **Progress Tracking**: Call requirements-tracker after each phase
5. **Lifecycle Management**:
   - Set `.current-requirement` when starting
   - Maintain during active development
   - Clear after production merge
   - Save progress before switching

### Common Workflows

#### New Feature Development
```javascript
// 1. User requests feature
// 2. Trigger central-coordinator
Task({
  subagent_type: "central-coordinator",
  description: "Orchestrate new feature",
  prompt: "Plan implementation for [feature description]"
})
// 3. Coordinator returns FEATURE workflow plan
// 4. Follow instructions to trigger each specialist
// 5. Call requirements-tracker between phases
```

#### Bug Fix
```javascript
// 1. User reports bug
// 2. Trigger central-coordinator
// 3. Coordinator selects BUG workflow (skips requirements gathering)
// 4. Direct to root cause analysis → implementation → testing
```

#### Emergency Hotfix
```javascript
// 1. Critical issue reported
// 2. Trigger central-coordinator with urgency flag
// 3. Coordinator selects HOTFIX workflow
// 4. Minimal gates: immediate fix → critical tests → emergency merge
```

# EdgeAlertSolution

This solution contains two related projects for the EdgeAlert system - a real-time desktop alerting system for medical facilities with comprehensive asset management, user monitoring, and administrative capabilities.

## Projects

### 1. EdgeAlertSignalClient (WPF Desktop Application)
- **Location**: `EdgeAlertSignalClient/`
- **Type**: WPF Desktop Application for Windows
- **Target Framework**: .NET 7.0 Windows
- **Purpose**: Windows desktop client application for medical staff
- **Key Features**:
  - Real-time alerting system with SignalR client connectivity
  - Comprehensive asset management with CSV import/export
  - User state monitoring (Active/Away/Offline) with automatic detection
  - Administrative interface with role-based access control
  - Advanced reporting and analytics with Excel export
  - Audio notifications and visual alert indicators
  - System tray integration for background operation
  - Network monitoring and IP address detection
  - Feature switch management for runtime configuration
  - Azure integration (Table Storage, Key Vault, SignalR Service)

### 2. EdgeAlertFunc (Azure Functions)
- **Location**: `EdgeAlertFunc/`
- **Type**: Azure Functions (Serverless)
- **Target Framework**: .NET 6.0
- **Purpose**: Azure Function App for server-side logic and data orchestration
- **Key Features**:
  - Azure Functions v4 with HTTP triggers
  - SignalR Service integration for real-time messaging
  - Azure Table Storage for data persistence and asset management
  - User authentication and authorization endpoints
  - Asset management APIs with CSV processing
  - Global settings and configuration management
  - Real-time message broadcasting and coordination

## Development Commands

### Build Commands

#### Linux Container Environment (Development)
```bash
# Build only the Azure Functions project (works on Linux)
dotnet build EdgeAlertFunc/EdgeAlertFunc.csproj

# Run Azure Functions locally for testing backend
cd EdgeAlertFunc && func start

# Test Azure Functions endpoints
curl -X POST http://localhost:7071/api/your-function-endpoint
```

#### Windows Environment (WPF Testing)
```bash
# Build entire solution (Windows only)
dotnet build EdgeAlertSolution.sln

# Debug WPF client (Windows only)
dotnet run --project EdgeAlertSignalClient/EdgeAlertSignalClient.csproj

# Build WPF client for release
dotnet build EdgeAlertSignalClient/EdgeAlertSignalClient.csproj --configuration Release
```

#### Git Workflow for Cross-Platform Development
```bash
# In Linux container - push changes for Windows testing
git add .
git commit -m "Feature implementation updates"
git push origin feature-branch-name

# On Windows machine - pull and test
git checkout feature-branch-name
git pull origin feature-branch-name
# Test WPF application
dotnet run --project EdgeAlertSignalClient/EdgeAlertSignalClient.csproj
```

### Testing
- No automated test framework is currently configured
- Manual testing required for both projects

## Architecture Overview

### WPF Client Architecture
The WPF client follows strict MVVM pattern with comprehensive separation of concerns:

**Views & UI Components**:
- `MainWindow.xaml` - Primary application window with navigation
- `AdminLoginView.xaml` - Administrative authentication interface
- Custom controls and converters for data binding and UI state management
- Alert visual indicators and audio notification system

**ViewModels** (Business Logic Layer):
- `MainWindowViewModel` - Central application coordination and SignalR connection management
- `AdminViewModel` - Administrative functions and system configuration
- `ReportsViewModel` - Data reporting, analytics, and Excel export coordination
- `AssetManagementViewModel` - Asset tracking and management operations
- `FeatureSwitchManagementViewModel` - Runtime feature configuration
- `AwaySettingsViewModel` - User state and availability management
- `ReportAccessManagementViewModel` - Role-based access control for reporting
- Uses CommunityToolkit.Mvvm for ObservableObject, ICommand, and validation

**Services** (Data Access & External Communication):
- `EdgeAlertService` - Primary HTTP/SignalR communication with Azure Functions
- `AssetTableService` - Asset data management and Azure Table Storage operations
- `UserTimeLogService` - User activity tracking and time logging
- `UserStateMonitorService` - Automatic user activity state detection (Active/Away/Offline)
- `AudioDeviceService` - Audio notification management and device selection
- `ExcelExportService` - Report generation and Excel file creation
- `TaskbarIconService` - System tray integration and background operation

**Stores** (Centralized State Management):
- `AlertStore` - Current alert state and active notifications
- `RoomStore` - Room/location information and spatial organization
- `NavigationStore` - UI navigation state and view management

**Handlers** (Specialized Operations):
- `AzureHandler` - Azure Key Vault integration and cloud authentication
- `NetworkHandler` - IP address detection and network configuration
- `IPAddressMonitor` - Continuous network monitoring and connectivity status
- `CommandLineArgsHolder` - Application startup parameter management

**Models & Data Structures**:
- `AssetItem` - Asset representation and properties
- `EdgeUser` - User account and profile information
- `UserStateModel` - User activity states and availability status
- `ReportPermission` - Access control for reporting features
- `DetailedReportData` - Comprehensive reporting data structures

**Infrastructure**:
- `TaskExtension` - Asynchronous operation utilities
- `DispatcherHelper` - UI thread marshaling for responsive interface
- Value converters for XAML data binding transformations

### Azure Functions Architecture
Server-side functions provide comprehensive backend services:

**Core Functions**:
- `EdgeAlertFunc.cs` - Main function app with HTTP triggers for client communication
- SignalR hub management and real-time message broadcasting
- Azure Table Storage CRUD operations for persistent data

**Asset Management**:
- `AssetManagement/AssetFunctions.cs` - Asset lifecycle management endpoints
- `AssetManagement/AssetEntity.cs` - Asset data model and table entity
- `AssetCsvFunctions.cs` - CSV import/export processing for bulk asset operations

**Data Services**:
- `AssetManagement/` module - Comprehensive asset tracking and management
- `GlobalSettingsTable.cs` - System-wide configuration management
- `ResponderTables.cs` - User and responder data management
- User authentication and authorization endpoints
- Real-time alert coordination and message routing

### Communication Flow
1. WPF client connects to Azure Functions via SignalR
2. Client authenticates and registers with room/location
3. Alert triggers broadcast to all connected clients in scope
4. Clients receive real-time notifications and update UI
5. User responses tracked in Azure Table Storage

## Key Configuration

### WPF Client Configuration
- `appsettings.json` - Application settings and logging configuration
- Azure Key Vault stores sensitive configuration (connection strings, function keys)
- User Secrets for development settings

### Azure Functions Configuration
- `host.json` - Azure Functions runtime configuration
- `local.settings.json` - Local development settings (not committed)
- Application Insights integration for monitoring

### Platform Limitations & Development Workflow
- **WPF Limitation**: WPF project requires Windows Desktop SDK - cannot build or run on Linux
- **Development Environment**: Current development occurs in Linux Docker container on Windows devbox
- **Testing Workflow**: 
  1. Develop and modify code in Linux container environment
  2. Push changes to git branch
  3. Pull branch to Windows machine for WPF client testing
  4. Test WPF application on Windows environment
  5. Return to Linux container for further development
- **Azure Functions**: Can be developed and tested on Linux/macOS using Azure Functions Core Tools
- **Backend Testing**: Azure Functions can be fully tested in the Linux container environment
- **Local Development**: Uses Azurite for Azure Storage emulation in container

### Security Considerations
- Azure Key Vault integration for secure configuration storage
- Role-based access control with administrative login system
- User authentication and authorization through Azure services
- Network-based location detection for access control
- Secure connection strings and API keys management

### Data Storage & Persistence
- Azure Table Storage for user data, alerts, asset management, and reporting
- Partitioned by ODS (Organizational Data Store) codes for multi-tenancy
- Time-based partitioning for performance optimization
- Local Azurite emulation for development (`__azurite_db_*.json` files)
- CSV import/export capabilities for bulk data operations

### Performance & Scalability
- Serverless Azure Functions backend for automatic scaling
- SignalR Service for efficient real-time communication
- Asynchronous programming patterns throughout WPF client
- Background services for continuous monitoring and state management

## Common Development Patterns

### MVVM Implementation
- ViewModels inherit from `ObservableValidator` (CommunityToolkit.Mvvm)
- Commands implemented using `IRelayCommand`
- Messaging between ViewModels using `IMessenger`

### Dependency Injection
- Services registered in `App.xaml.cs`
- Constructor injection throughout the application
- Singleton pattern for stores and handlers

### Error Handling
- Polly for retry policies in HTTP communication
- Serilog for structured logging
- Azure Application Insights for monitoring

### Real-time Communication
- SignalR hub connection management in `EdgeAlertService`
- Automatic reconnection with exponential backoff
- Message queuing for offline scenarios
- Azure SignalR Service for scalable real-time messaging

## Project Structure Details

### EdgeAlertSignalClient File Organization
```
EdgeAlertSignalClient/
├── Views/
│   ├── MainWindow.xaml - Primary application interface
│   └── AdminLoginView.xaml - Administrative access portal
├── ViewModels/
│   ├── MainWindowViewModel.cs - Application coordination
│   ├── AdminViewModel.cs - Administrative operations
│   ├── ReportsViewModel.cs - Analytics and reporting
│   ├── AssetManagementViewModel.cs - Asset lifecycle management
│   ├── FeatureSwitchManagementViewModel.cs - Runtime configuration
│   ├── AwaySettingsViewModel.cs - User availability settings
│   └── ReportAccessManagementViewModel.cs - Access control
├── Services/
│   ├── EdgeAlertService.cs - Primary communication service
│   ├── AssetTableService.cs - Asset data operations
│   ├── UserTimeLogService.cs - Activity tracking
│   ├── UserStateMonitorService.cs - State detection
│   ├── AudioDeviceService.cs - Audio management
│   ├── ExcelExportService.cs - Report generation
│   └── TaskbarIconService.cs - System tray integration
├── Stores/
│   ├── AlertStore.cs - Alert state management
│   ├── RoomStore.cs - Location information
│   └── NavigationStore.cs - UI navigation
├── Handlers/
│   ├── AzureHandler.cs - Cloud service integration
│   ├── NetworkHandler.cs - Network operations
│   ├── IPAddressMonitor.cs - Connectivity monitoring
│   └── CommandLineArgsHolder.cs - Startup parameters
├── Models/
│   ├── AssetItem.cs - Asset data structure
│   ├── EdgeUser.cs - User representation
│   ├── UserStateModel.cs - Activity states
│   ├── ReportPermission.cs - Access permissions
│   └── DetailedReportData.cs - Reporting structures
├── Converters/ - XAML data binding converters
├── Resources/ - Application resources and assets
└── Configuration files (appsettings.json, App.config)
```

### EdgeAlertFunc File Organization
```
EdgeAlertFunc/
├── EdgeAlertFunc.cs - Main function definitions
├── AssetManagement/
│   ├── AssetFunctions.cs - Asset API endpoints
│   └── AssetEntity.cs - Asset data model
├── AssetCsvFunctions.cs - CSV processing functions
├── GlobalSettingsTable.cs - Configuration management
├── ResponderTables.cs - User data services
├── host.json - Function app configuration
└── local.settings.json - Development settings (not in source control)
```

## Integration Points

### Client-Server Communication
1. **Authentication Flow**: Client authenticates via Azure AD, receives tokens for API access
2. **SignalR Connection**: Persistent WebSocket connection for real-time messaging
3. **HTTP API Calls**: RESTful endpoints for data operations (CRUD, reports, configuration)
4. **Asset Management**: Bidirectional sync for asset data with CSV import/export support
5. **User State Sync**: Real-time user availability updates across all connected clients

### Data Flow Architecture
1. **Client State Changes** → Azure Functions → Azure Table Storage → SignalR broadcast
2. **Alert Triggers** → Immediate SignalR broadcast → Client UI updates → Audio notifications
3. **Asset Updates** → REST API → Azure Table Storage → Real-time sync to all clients
4. **Report Generation** → Client requests → Azure Functions query → Excel export locally
```