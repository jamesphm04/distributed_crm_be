// be's Container App (the API, 2 replicas) and its migration job, deployed by .github/workflows/deploy.yml into the
// resource group the distributed_crm_infra stack created. Shared names and IDs come from that stack's outputs.

@description('Azure region for every resource.')
param location string = resourceGroup().location

@description('Tags applied to every resource.')
param tags object = {
  project: 'distributed-crm'
  purpose: 'demo'
  owner: 'james'
}

@description('Image tag to deploy: the git commit SHA the images were built from.')
param imageTag string

@description('The dcrm-dev stack\'s outputs, as name: value.')
param stack stackOutputs

// The pipeline deploys twice: first only the migration job (false), runs it, and only after it succeeds deploys the
// API (true). The new API version then never runs against a schema that hasn't been migrated.
@description('Whether to deploy the API, or only the migration job.')
param deployApi bool = true

// The outputs this template uses. Others the stack has are ignored.
type stackOutputs = {
  containerAppsEnvironmentId: string
  registryLoginServer: string
  keyVaultUri: string
  postgresHost: string
  postgresDatabaseName: string
  redisHost: string
  serviceBusNamespace: string
  appInsightsConnectionString: string
  beIdentityId: string
  beIdentityName: string
  beClientId: string
  workerIdentityName: string
  migrateIdentityId: string
  migrateIdentityName: string
  migrateClientId: string
}

var apiPort = 8080

resource api 'Microsoft.App/containerApps@2025-07-01' = if (deployApi) {
  name: 'ca-dcrm-be-dev'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${stack.beIdentityId}': {}
    }
  }
  properties: {
    environmentId: stack.containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      // One revision at a time: a deploy replaces the running version instead of splitting traffic.
      activeRevisionsMode: 'Single'
      ingress: {
        // Public HTTPS. Container Apps terminates TLS and forwards plain HTTP with X-Forwarded-* headers.
        external: true
        targetPort: apiPort
        transport: 'auto'
        allowInsecure: false
      }
      // Pulls with the app's identity (AcrPull), so the registry needs no admin user or password.
      registries: [
        {
          server: stack.registryLoginServer
          identity: stack.beIdentityId
        }
      ]
      // Fetched from Key Vault by the app's identity (Key Vault Secrets User); the values never pass through here.
      secrets: [
        {
          name: 'google-client-id'
          keyVaultUrl: '${stack.keyVaultUri}secrets/google-client-id'
          identity: stack.beIdentityId
        }
        {
          name: 'allowed-email-0'
          keyVaultUrl: '${stack.keyVaultUri}secrets/allowed-email-0'
          identity: stack.beIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${stack.registryLoginServer}/distributed-crm-be:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            // No password or key in any of these three, so each signs in with a token for the app's identity.
            {
              name: 'ConnectionStrings__DistributedCrm'
              value: 'Host=${stack.postgresHost};Database=${stack.postgresDatabaseName};Username=${stack.beIdentityName};Ssl Mode=Require'
            }
            {
              name: 'ConnectionStrings__Redis'
              value: '${stack.redisHost}:10000,ssl=true'
            }
            {
              name: 'ConnectionStrings__ServiceBus'
              value: stack.serviceBusNamespace
            }
            // Which user-assigned identity to sign in as, and only ever as a managed identity.
            {
              name: 'AZURE_CLIENT_ID'
              value: stack.beClientId
            }
            {
              name: 'AZURE_TOKEN_CREDENTIALS'
              value: 'ManagedIdentityCredential'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: stack.appInsightsConnectionString
            }
            {
              name: 'GoogleAuth__ClientId'
              secretRef: 'google-client-id'
            }
            {
              name: 'GoogleAuth__AllowedEmails__0'
              secretRef: 'allowed-email-0'
            }
          ]
          probes: [
            // Holds off the other probes until the app is listening: up to 60 s for a cold start.
            {
              type: 'Startup'
              httpGet: {
                path: '/health/live'
                port: apiPort
              }
              periodSeconds: 2
              failureThreshold: 30
            }
            // Restarts a hung process. Checks no dependency, so an outage elsewhere never restarts it.
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: apiPort
              }
              periodSeconds: 10
              failureThreshold: 3
            }
            // Takes a replica out of traffic while it can't reach Postgres. Longer than the app's own 5 s check
            // timeout, so a slow check reports its 503 instead of timing the probe out.
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: apiPort
              }
              periodSeconds: 5
              timeoutSeconds: 6
              failureThreshold: 3
            }
          ]
        }
      ]
      // Two replicas always, so every test proves no request depends on one process's memory.
      scale: {
        minReplicas: 2
        maxReplicas: 2
      }
    }
  }
}

// Applies the schema and the apps' database roles. Started by the pipeline before each API deploy; a failed run
// stops the pipeline, so the new API version never meets a schema it doesn't expect.
resource migrate 'Microsoft.App/jobs@2025-07-01' = {
  name: 'caj-dcrm-migrate-dev'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${stack.migrateIdentityId}': {}
    }
  }
  properties: {
    environmentId: stack.containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      replicaTimeout: 600
      // The script is idempotent, so one retry after a transient failure is safe.
      replicaRetryLimit: 1
      registries: [
        {
          server: stack.registryLoginServer
          identity: stack.migrateIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: '${stack.registryLoginServer}/distributed-crm-migrate:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          // No PGPASSWORD: migrate.sh asks the job's identity for a token. It signs in as the identity's
          // Postgres role, an Entra admin, and creates a role for each app identity.
          env: [
            {
              name: 'PGHOST'
              value: stack.postgresHost
            }
            {
              name: 'PGDATABASE'
              value: stack.postgresDatabaseName
            }
            {
              name: 'PGUSER'
              value: stack.migrateIdentityName
            }
            {
              name: 'PGSSLMODE'
              value: 'require'
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: stack.migrateClientId
            }
            {
              name: 'APP_DB_ROLES'
              value: '${stack.beIdentityName} ${stack.workerIdentityName}'
            }
          ]
        }
      ]
    }
  }
}

output apiUrl string = deployApi ? 'https://${api!.properties.configuration.ingress.fqdn}' : ''
output migrateJobName string = migrate.name
