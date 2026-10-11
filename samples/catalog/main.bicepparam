using 'main.bicep'

param apiKey = readEnvironmentVariable('INCIDENT_IO_API_KEY')
param baseUrl = readEnvironmentVariable('INCIDENT_IO_BASE_URL', 'https://api.incident.io')
