targetScope = 'local'

@secure()
param apiKey string

param baseUrl string = 'https://api.incident.io'

@description('Services to register in the catalog.')
param services array = [
  { id: 'payments-api', name: 'Payments API', tier: '1', team: 'payments', runbooks: ['https://runbooks.example.com/payments'] }
  { id: 'checkout-web', name: 'Checkout Web', tier: '2', team: 'payments', runbooks: [] }
  { id: 'search', name: 'Search', tier: '2', team: 'discovery', runbooks: [] }
]

extension incidentio with {
  apiKey: apiKey
  baseUrl: baseUrl
}

resource teamType 'CatalogType' = {
  name: 'Engineering Team'
  typeName: 'Custom["EngineeringTeam"]'
  description: 'Engineering teams, managed in Bicep.'
  color: 'violet'
  icon: 'users'
  categories: ['team']
  sourceRepoUrl: 'https://github.com/example/incident-config'
  attributes: [
    { name: 'Slack channel', type: 'String' }
  ]
}

resource serviceType 'CatalogType' = {
  name: 'Service'
  typeName: 'Custom["Service"]'
  description: 'Production services, managed in Bicep.'
  color: 'blue'
  icon: 'server'
  categories: ['service']
  sourceRepoUrl: 'https://github.com/example/incident-config'
  annotations: {
    'managed-by': 'bicep'
  }
  attributes: [
    { name: 'Tier', type: 'String' }
    { name: 'Owner', type: teamType.typeName }
    { name: 'Runbooks', type: 'String', array: true }
  ]
}

resource teams 'CatalogEntry' = [
  for team in ['payments', 'discovery']: {
    catalogTypeId: teamType.id
    externalId: team
    name: '${toUpper(substring(team, 0, 1))}${substring(team, 1)}'
    attributeValues: {
      'Slack channel': { value: '#team-${team}' }
    }
  }
]

resource serviceEntries 'CatalogEntry' = [
  for svc in services: {
    catalogTypeId: serviceType.id
    externalId: svc.id
    name: svc.name
    aliases: [svc.id]
    attributeValues: {
      Tier: { value: svc.tier }
      // References to other catalog entries accept the entry's external ID.
      Owner: { value: svc.team }
      Runbooks: { arrayValue: svc.runbooks }
    }
    dependsOn: [teams]
  }
]

// A custom field whose options come from the Service catalog.
resource affectedServices 'CustomField' = {
  name: 'Affected Services'
  description: 'Which services are affected by this incident?'
  fieldType: 'multi_select'
  catalogTypeId: serviceType.id
}

output serviceTypeId string = serviceType.id
output serviceAttributeIds object = serviceType.attributeIds
output affectedServicesFieldId string = affectedServices.id
