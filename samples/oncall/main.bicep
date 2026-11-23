targetScope = 'local'

@secure()
param apiKey string

param baseUrl string = 'https://api.incident.io'

@description('Emails of the people in the primary rotation, in shift order.')
param primaryEngineers array = [
  'alice@example.com'
  'bob@example.com'
]

extension incidentio with {
  apiKey: apiKey
  baseUrl: baseUrl
}

// Users are looked up, never created (Terraform: data "incident_user").
resource engineers 'User' existing = [
  for email in primaryEngineers: {
    email: email
  }
]

resource platformSchedule 'Schedule' = {
  name: 'Platform on-call'
  timezone: 'Europe/Stockholm'
  holidaysPublicConfig: {
    countryCodes: ['SE']
  }
}

// Weekly rotation, handing over Monday 09:00.
resource primary 'ScheduleRotation' = {
  scheduleId: platformSchedule.id
  name: 'Primary'
  firstIntervalStartsAt: '2026-01-05T08:00:00Z'
  handovers: [
    { interval: 1, intervalType: 'weekly' }
  ]
  users: [for (email, i) in primaryEngineers: engineers[i].id]
  // Line-up changes wait for the current shift to finish. Unchanged rotations are never rewritten.
  rollout: 'after_current_shift'
}

// Business-hours-only secondary.
resource secondary 'ScheduleRotation' = {
  scheduleId: platformSchedule.id
  name: 'Secondary (office hours)'
  firstIntervalStartsAt: '2026-01-05T08:00:00Z'
  handovers: [
    { interval: 1, intervalType: 'daily' }
  ]
  users: [engineers[1].id, engineers[0].id]
  schedulingMode: 'sequential'
  workingIntervals: [
    for day in ['monday', 'tuesday', 'wednesday', 'thursday', 'friday']: {
      weekday: day
      startTime: '09:00'
      endTime: '17:00'
    }
  ]
}

// Last resort: page the secondary rotation's schedule directly.
resource fallback 'EscalationPath' = {
  name: 'Platform fallback'
  start: 'main'
  sequences: {
    main: {
      nodes: [
        {
          level: {
            targets: [{ type: 'schedule', id: platformSchedule.id, urgency: 'high', scheduleMode: 'all_users' }]
            timeToAckSeconds: 600
          }
        }
      ]
    }
  }
}

// In office hours page high urgency and repeat; out of hours page low urgency, then hand over.
resource platform 'EscalationPath' = {
  name: 'Platform'
  start: 'main'
  sequences: {
    main: {
      nodes: [
        {
          id: 'start'
          branch: {
            if: { workingHoursActive: 'office' }
            then: 'in_hours'
            else: 'out_of_hours'
          }
        }
      ]
    }
    in_hours: {
      nodes: [
        {
          level: {
            targets: [{ type: 'schedule', id: platformSchedule.id, urgency: 'high', scheduleMode: 'currently_on_call' }]
            timeToAckSeconds: 300
            ackMode: 'first'
          }
        }
        { delay: { delaySeconds: 120 } }
        { loop: { backTo: 'start', times: 2 } }
      ]
    }
    out_of_hours: {
      nodes: [
        {
          level: {
            targets: [{ type: 'user', id: engineers[0].id, urgency: 'low' }]
            timeToAckSeconds: 900
          }
        }
        { escalationPath: { escalationPathId: fallback.id } }
      ]
    }
  }
  workingHours: [
    {
      id: 'office'
      name: 'Office hours'
      timezone: 'Europe/Stockholm'
      weekdayIntervals: [
        for day in ['monday', 'tuesday', 'wednesday', 'thursday', 'friday']: {
          weekday: day
          startTime: '08:00'
          endTime: '18:00'
        }
      ]
    }
  ]
  repeatConfig: {
    delayRepeatOnActivity: true
    repeatAfterSeconds: 3600
  }
}

output scheduleId string = platformSchedule.id
output primaryRotationId string = primary.id
output escalationPathId string = platform.id
