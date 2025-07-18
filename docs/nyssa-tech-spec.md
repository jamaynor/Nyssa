
# Nyssa Technical Design Document

**Document Version:** 1.0  
**Last Updated:** July 17, 2025  
**Prepared By:** Jeremy Maynor  
**Classification:** Internal Use

---

## Table of Contents

1. [System Architecture Overview](#system-architecture-overview)
2. [Service Architecture](#service-architecture)
3. [Database Architecture](#database-architecture)
4. [API Design](#api-design)
5. [Caching Strategy](#caching-strategy)
6. [Database Schema](#database-schema) *(To Be Completed)*
7. [Integration Architecture](#integration-architecture) *(To Be Completed)*
8. [Security & Authentication](#security--authentication) *(To Be Completed)*
9. [Infrastructure & Deployment](#infrastructure--deployment) *(To Be Completed)*

---

## System Architecture Overview

### High-Level Architecture

Nyssa is built as a **microservices architecture** with clear separation of concerns across voice processing, task orchestration, and integration management. The system follows a **multi-tenant SaaS model** with organization-scoped data isolation.

### Technology Stack

**Frontend:**
- **Web Application:** Blazor WASM (WebAssembly) for rich client-side experience
- **Mobile Application:** Blazor inside MAUI (Multi-platform App UI) - shared UI code across platforms
- **Backend for Frontend (BFF):** Model Context Protocol (MCP) server fronting all Blazor requests
- **Real-time Updates:** SignalR for live status updates
- **Voice Input:** Native platform speech recognition integrated with Blazor

**Backend Services:**
- **Runtime:** .NET 10 with C#
- **BFF Layer:** Model Context Protocol server with MassTransit service bus for internal coordination
- **Core API:** ASP.NET Core Web API behind MCP server
- **Database:** PostgreSQL with multi-tenant schema
- **Cache:** Redis for session management and performance
- **Message Queue:** MassTransit for asynchronous service communication

**Voice Processing:**
- **Speech-to-Text:** 11Labs or alternative voice processing APIs
- **Local AI Model:** Custom voice processing hosted on same infrastructure
- **Voice Pipeline:** Raw audio → Local AI → Structured commands → Task creation

**Infrastructure:**
- **Cloud Platform:** Fly.io (cost-effective container deployment)
- **Container Orchestration:** Fly.io's built-in container management
- **Database Hosting:** Fly.io PostgreSQL
- **Monitoring:** Fly.io built-in monitoring + custom application logging

### Architecture Pattern

```
Blazor WASM (Web) ──┐
                    ├── Model Context Protocol Server (BFF) ── Core .NET Services
Blazor MAUI (Mobile) ┘      │
                            └── MassTransit Service Bus
```

### Voice Processing Pipeline

```
Blazor Client (Web/Mobile) 
    ↓ [Raw Voice Data]
MCP Server (BFF)
    ↓ [Raw Voice Data] 
Local AI Model (Fly.io containers)
    ↓ [Structured Commands]
Task Orchestration Service
    ↓ [Validated Tasks]
Integration Layer (Monday.com, Google Tasks, Microsoft 365)
```

**Benefits of Local AI:**
- **Latency:** Sub-second processing vs. external API round-trips
- **Cost Control:** Fixed infrastructure costs vs. per-request API pricing
- **Privacy:** Voice data never leaves your infrastructure environment
- **Customization:** Fine-tuned models for business domain and executive speech patterns
- **Reliability:** No external API dependencies for core voice processing

---

## Service Architecture

### Feature-to-Service Mapping

Based on the PRD epics, Nyssa consists of **6 core services** that communicate via MassTransit messages:

#### 1. Voice Processing Service
**Responsibilities:**
- Convert raw audio to structured commands using local AI model
- Validate command completeness and context
- Extract intent, entities, and business context

**Epic Coverage:** Epic 1 (Voice Delegation Capture)

#### 2. Routing Service  
**Responsibilities:**
- User lookup and department mapping
- Department-to-tool configuration (Marketing → Monday.com, Engineering → Microsoft 365 Tasks)
- Task creation in appropriate external systems

**Epic Coverage:** Epic 3 (Intelligent Routing & Assignment)

#### 3. Team Management Service
**Responsibilities:**
- BD team member assignments and workload tracking
- Internal task assignment and capacity planning
- Team coordination and availability management

**Epic Coverage:** Epic 2 (Internal BD Team Management)

#### 4. Request Management Service
**Responsibilities:**
- Handle inbound requests from other departments to BD team
- BD VP approval and triage workflow
- Request assignment to appropriate team members

**Epic Coverage:** Epic 4 (Inbound Request Management & Adjudication)

#### 5. Conflict Detection Service
**Responsibilities:**
- Identify duplicate or conflicting requests across all work streams
- Priority conflict resolution and authentic priority management
- Cross-team work visibility and coordination intelligence

**Epic Coverage:** Epic 5 (Conflict Prevention & Team Coordination)

#### 6. Analytics Service
**Responsibilities:**
- Usage pattern tracking and productivity metrics
- Executive dashboard and reporting
- Automatic escalation when coordination breaks down

**Epic Coverage:** Epic 6 (CEO Strategic Oversight)

### BFF Service Orchestration Pattern

The **Model Context Protocol Server (BFF)** acts as an orchestrator that delegates to specialized services using **MassTransit** for loose coupling:

```
Blazor Clients (Web/Mobile)
    ↓ [MCP Requests]
BFF Server (MCP + MassTransit)
    ├── MCP Interface Layer
    ├── MassTransit Service Bus
    │   ├── Voice Processing Service
    │   ├── Routing Service  
    │   ├── Team Management Service
    │   ├── Request Management Service
    │   ├── Conflict Detection Service
    │   └── Analytics Service
    └── PostgreSQL Database
```

### Service Communication Benefits

**MassTransit Advantages:**
- **Start Monolithic:** All services in one process initially for development speed
- **Easy Separation:** Move services to separate containers later without code changes
- **Loose Coupling:** Services communicate via messages, not direct calls
- **Scalability:** Can distribute load across service instances as needed
- **Resilience:** Built-in retry policies and error handling

---

## Database Architecture

### Multi-Tenant Strategy

**Approach:** Monolithic PostgreSQL database with **schema separation per service**

**Rationale:**
- **MVP Speed:** Single database to manage and deploy
- **Multi-Tenant Simplicity:** Easier to implement organization-scoped data isolation
- **ACID Consistency:** Maintain data consistency across services
- **Migration Path:** Can split databases later as services scale independently

### Schema Organization

```sql
-- Each service gets its own schema
voice_processing.*     -- Voice Processing Service
routing.*             -- Routing Service  
team_management.*     -- Team Management Service
request_management.*  -- Request Management Service
conflict_detection.*  -- Conflict Detection Service
analytics.*           -- Analytics Service
```

### Cross-Service Reference Strategy

**No Foreign Key Dependencies Across Schemas:**
- Store cross-service references as regular UUID fields (not foreign keys)
- Use application-level validation instead of database constraints
- Service calls to validate references exist
- Events to maintain data consistency

**Example:**
```sql
-- GOOD: Store ID without foreign key constraint
team_management.team_members (
    id UUID PRIMARY KEY,
    user_id UUID,  -- References routing.users.id but no FK constraint
    team_id UUID,
    created_at TIMESTAMP
);
```

**Benefits:**
- **Future Database Splitting:** No foreign key constraints to break
- **Service Independence:** Services can be moved to separate databases easily
- **Loose Coupling:** Database schema reflects service boundaries

---

## API Design

### Communication Architecture

**External Communication:** Model Context Protocol (MCP)
- Blazor clients communicate with BFF via MCP interface
- Synchronous request/response for user interactions
- Optimized for frontend data requirements

**Internal Communication:** MassTransit Messages
- BFF coordinates backend services via message bus
- Asynchronous processing for complex operations
- Event-driven coordination between services

### API Boundaries

**BFF Responsibilities:**
- **MCP Interface:** Handle all client requests
- **Service Orchestration:** Delegate to appropriate backend services via MassTransit
- **Data Aggregation:** Combine responses from multiple services
- **Caching:** Cache frequently accessed data
- **Error Handling:** Centralized error management for clients

**Service Communication Pattern:**
```csharp
// BFF receives MCP request, publishes MassTransit message
await bus.Publish<ProcessVoiceCommand>(new {
    VoiceData = rawAudio,
    UserId = userId,
    OrganizationId = orgId
});

// Service consumes message and processes
public class ProcessVoiceCommandConsumer : IConsumer<ProcessVoiceCommand>
{
    public async Task Consume(ConsumeContext<ProcessVoiceCommand> context)
    {
        // Process voice with local AI
        // Return structured command via response or event
    }
}
```

---

## Caching Strategy

### Cross-Service Lookup Optimization

**Cache Targets (Most Frequently Accessed):**
- **User lookups:** User ID → name, department, permissions
- **Department configurations:** Department → tool mappings (Monday.com, Google Tasks, Microsoft 365)
- **Team membership:** User → team assignments and roles

### Caching Layers

**1. Local In-Memory Cache (L1)**
- **Technology:** .NET MemoryCache per service instance
- **TTL:** 5-15 minutes depending on data volatility
- **Use Case:** Hot data for immediate lookups

**2. Distributed Cache (L2)**
- **Technology:** Redis on Fly.io
- **TTL:** 15 minutes to 1 hour depending on data type
- **Use Case:** Shared cache across service instances

### Cache Invalidation Strategy

**Event-Driven Invalidation:**
- When data changes, publish MassTransit event
- All services consuming the event clear relevant cache entries
- Ensures cache consistency across service instances

**TTL Strategy by Data Type:**
- **User data:** 5-15 minutes (changes infrequently)
- **Department configurations:** 1 hour (rarely changes)
- **Team membership:** 10 minutes (moderate change frequency)

**Benefits:**
- **Fast Cross-Service Lookups:** Sub-millisecond cache hits
- **Reduced Database Load:** Frequently accessed data served from cache
- **Eventual Consistency:** Acceptable for most business operations
- **Service Independence:** Each service manages its own cache invalidation

---

## Integration Requirements

### Core Platform Integrations (MVP)

**Required Initial Integrations:**
1. **Monday.com** - Project management and task tracking
2. **Google Tasks** - Google Workspace task management
3. **Microsoft 365 Tasks** - Microsoft Office task management

**Integration Approach:**
- **Universal Task Creation:** Abstract task creation across all three platforms
- **Authentication Management:** Handle OAuth flows for each platform
- **Webhook Support:** Receive real-time updates from external systems
- **Error Handling:** Graceful degradation when integrations are unavailable

---

## Next Steps

### Remaining Design Areas

1. **Database Schema Design** - Detailed table structures for each service schema
2. **Integration Architecture** - Specific implementation details for Monday.com, Google Tasks, Microsoft 365 Tasks
3. **Security & Authentication** - Multi-tenant access control and data isolation
4. **Infrastructure & Deployment** - Fly.io container configuration and scaling strategy

### Implementation Approach

**Phase 1:** Complete technical design document
**Phase 2:** Setup development environment (Fly.io, PostgreSQL, Redis)
**Phase 3:** Build MVP services starting with Voice Processing and Routing
**Phase 4:** Add integration layer and remaining services

---

**Document Status:** In Progress  
**Completed Sections:** 1-5  
**Remaining Sections:** 6-9  
**Next Priority:** Database Schema Design