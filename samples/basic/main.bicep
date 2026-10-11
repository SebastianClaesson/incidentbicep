targetScope = 'local'

@secure()
@description('incident.io API key with permission to manage settings.')
param apiKey string

@description('Override only for testing against a mock server.')
param baseUrl string = 'https://api.incident.io'

extension incidentio with {
  apiKey: apiKey
  baseUrl: baseUrl
}

resource sevMinor 'Severity' = {
  name: 'Minor'
  description: 'Issues with low impact on customers. Fix in business hours.'
  rank: 1
}

resource sevMajor 'Severity' = {
  name: 'Major'
  description: 'Significant customer impact. Respond immediately.'
  rank: 2
}

resource commsLead 'IncidentRole' = {
  name: 'Communications Lead'
  shortform: 'comms'
  description: 'Owns internal and external communication during the incident.'
  instructions: 'Post an update to the status page and #incidents at least every 30 minutes.'
}

resource monitoring 'IncidentStatus' = {
  name: 'Monitoring'
  category: 'live'
  description: 'A fix is deployed and we are watching for recurrence.'
}

resource affectedArea 'CustomField' = {
  name: 'Affected Area'
  description: 'Which part of the product is affected?'
  fieldType: 'single_select'
}

resource areaOptions 'CustomFieldOption' = [
  for (area, i) in ['API', 'Web app', 'Mobile app']: {
    customFieldId: affectedArea.id
    value: area
    sortKey: (i + 1) * 10
  }
]

// Reference a resource that already exists (e.g. created in the UI) to read its ID.
resource critical 'Severity' existing = {
  name: 'Critical'
}

output majorSeverityId string = sevMajor.id
output criticalSeverityId string = critical.id
output affectedAreaFieldId string = affectedArea.id
