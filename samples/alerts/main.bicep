targetScope = 'local'

@secure()
param apiKey string

param baseUrl string = 'https://api.incident.io'

@description('Email of the maintenance window lead.')
param leadEmail string = 'alice@example.com'

extension incidentio with {
  apiKey: apiKey
  baseUrl: baseUrl
}

resource lead 'User' existing = {
  email: leadEmail
}

// Generic HTTP source. Title and description come from the payload; 'team' is derived by an expression.
resource httpSource 'AlertSource' = {
  name: 'Platform HTTP alerts'
  sourceType: 'http'
  title: { valueReference: 'payload.title' }
  description: { valueLiteral: '{{ payload.description }}' }
  expressions: [
    {
      reference: 'team'
      label: 'Team from payload'
      rootReference: 'payload'
      operations: [
        {
          operationType: 'navigate'
          navigate: { reference: 'metadata.team' }
        }
      ]
      elseBranch: {
        result: { valueLiteral: 'platform' }
      }
    }
  ]
  // Ignore test events.
  filterConditionGroups: [
    {
      conditions: [
        {
          subject: 'payload.environment'
          operation: 'is_not'
          paramBindings: [{ valueLiteral: 'test' }]
        }
      ]
    }
  ]
  autoResolveTimeoutMinutes: 240
}

// Heartbeat source: alerts when the nightly backup job stops pinging.
resource backupHeartbeat 'AlertSource' = {
  name: 'Nightly backup heartbeat'
  sourceType: 'heartbeat'
  title: { valueLiteral: 'Nightly backup missed its heartbeat' }
  heartbeatOptions: {
    intervalSeconds: 86400
    gracePeriodSeconds: 3600
    failureThreshold: 1
  }
}

// Hold back alerts from the HTTP source during the planned database upgrade.
resource dbUpgrade 'MaintenanceWindow' = {
  name: 'Database upgrade 2026-11'
  startAt: '2026-11-14T22:00:00Z'
  endAt: '2026-11-15T02:00:00Z'
  leadId: lead.id
  alertConditionGroups: [
    {
      conditions: [
        {
          subject: 'alert.source'
          operation: 'one_of'
          paramBindings: [{ values: [httpSource.id] }]
        }
      ]
    }
  ]
  notificationMessage: 'Planned database upgrade. Alerts from the platform source are held back.'
  resolveOnEnd: true
  showInSidebar: true
}

output alertEventsUrl string = httpSource.alertEventsUrl
output heartbeatPingUrl string = backupHeartbeat.heartbeatPingUrl
output maintenanceWindowId string = dbUpgrade.id
