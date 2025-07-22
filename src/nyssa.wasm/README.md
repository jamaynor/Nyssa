# Nyssa WASM Application

A modern Blazor WebAssembly application for managing role-based access control (RBAC) with a beautiful, responsive UI built with Tailwind CSS.

## Project Structure

The application follows a feature-based folder structure for better organization and maintainability:

```
src/nyssa.wasm/
├── Features/
│   ├── Home/                 # Marketing homepage components
│   │   ├── HomePage.razor
│   │   ├── HeroSection.razor
│   │   ├── SolutionsSection.razor
│   │   ├── ContactSection.razor
│   │   └── FooterSection.razor
│   ├── Dashboard/            # Dashboard overview
│   │   └── DashboardPage.razor
│   ├── Organizations/        # Organization management
│   │   └── OrganizationsPage.razor
│   ├── Users/               # User management
│   │   └── UsersPage.razor
│   ├── Roles/               # Role management
│   │   └── RolesPage.razor
│   ├── Audit/               # Audit log
│   │   └── AuditPage.razor
│   └── Shared/              # Shared components and layouts
│       ├── MainLayout.razor
│       ├── AppLayout.razor
│       ├── Navigation.razor
│       └── AppNavigation.razor
├── Styles/
│   └── app.css              # Tailwind CSS source
├── wwwroot/
│   ├── css/
│   │   └── app.css          # Compiled CSS
│   └── index.html
└── Program.cs
```

## Features

### Marketing Homepage
- Modern hero section with gradient background
- Solutions showcase with feature cards
- Contact form section
- Responsive footer with links

### Dashboard Application
- Overview metrics and statistics
- Recent activity feed
- Sidebar navigation
- User profile section

### Feature Pages (Coming Soon)
- Organizations management
- User administration
- Role and permission configuration
- Audit log viewer

## Getting Started

### Prerequisites
- .NET 8.0 SDK
- Node.js and npm

### Installation

1. Install dependencies:
```bash
npm install
```

2. Build CSS:
```bash
npm run build-css
```

3. Run the application:
```bash
dotnet run
```

### Development

For development with CSS hot-reload:

1. Start CSS watch mode:
```bash
npm run watch-css
```

2. In another terminal, run the application:
```bash
dotnet watch run
```

## Navigation

- **Home** (`/`): Marketing homepage
- **Dashboard** (`/app`): Main application dashboard
- **Organizations** (`/app/organizations`): Manage organizations
- **Users** (`/app/users`): User management
- **Roles** (`/app/roles`): Role configuration
- **Audit Log** (`/app/audit`): View audit trail

## Styling

The application uses Tailwind CSS with custom component classes defined in `Styles/app.css`. Key design principles:

- Mobile-first responsive design
- Consistent spacing scale (4, 8, 12, 16, 24, 32, 48, 64px)
- Custom color scheme with primary and secondary colors
- Reusable component classes (`.card`, `.btn`, `.form-input`, etc.)

## Architecture

Following Blazor WASM best practices:
- Feature-based folder structure
- Separate layouts for marketing and app sections
- Component-based architecture
- Clean separation of concerns

## Building for Production

```bash
# Build CSS
npm run build-css

# Build the application
dotnet publish -c Release
```

## Contributing

When adding new features:
1. Create a new folder under `Features/`
2. Follow the existing component patterns
3. Use Tailwind utility classes for styling
4. Ensure responsive design on all screen sizes