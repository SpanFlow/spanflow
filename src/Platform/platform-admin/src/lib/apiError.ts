import type { AxiosError } from 'axios'
import { serviceForUrl, type ServiceDescriptor } from './services'
import { useServiceStatusStore } from '../stores/serviceStatusStore'

/**
 * Turning an API failure into something worth reading (E08-T15).
 *
 * The reason this exists is 403. Since E16-T2 every DataFlow admin endpoint carries a permission
 * (`pipelines.edit`, `connections.use`, `workbench.execute`…), so "not allowed" is now an ordinary
 * outcome for a member with a narrow role — and every panel was reporting it as "failed to load — is
 * the DataFlow API running?", which is both untrue and sends the reader to restart a healthy service.
 * A permission denial is an *answer*: say who to ask, and stop.
 */

/** The E16-T2 permission codes, so a denial can name the one the reader needs to be granted. */
export const PERMISSIONS = {
  pipelinesView: 'pipelines.view',
  pipelinesEdit: 'pipelines.edit',
  pipelinesRun: 'pipelines.run',
  pipelinesPublish: 'pipelines.publish',
  connectionsView: 'connections.view',
  connectionsUse: 'connections.use',
  connectionsEdit: 'connections.edit',
  connectionsManageKeys: 'connections.manage_keys',
  workbenchExecute: 'workbench.execute',
  connectionsWriteData: 'connections.write_data',
  codeNodeEdit: 'code_node.edit',
  manifestsManage: 'manifests.manage',
  // The MCP data-gateway surface (E24) — building gateways/exposures/scopes and reading their audit.
  mcpGatewaysManage: 'mcp_gateways.manage',
  // Conversational flows (E25): designing, publishing and deploying a chat service.
  chatFlowsManage: 'chat_flows.manage',
  // Lending a mapping to another project is its own grant (E16-T6): editing a mapping changes what
  // this project runs, sharing one lets a different project run — or read — it.
  mappingsShare: 'mappings.share',
  viewAnalytics: 'project.view_analytics',
  // The AI policy's own grant (E29-T1): changing a token cap or a budget is a spending decision.
  aiManageSettings: 'ai.manage_settings',
  // Platform's own codes, for the panels served by the Platform API rather than DataFlow.
  orgManageProjects: 'org.manage_projects',
  orgManageMembers: 'org.manage_members',
  // Staffing one project is its own grant, held by ProjectAdmin and (since the OrgAdmin gap was closed)
  // OrgAdmin. Naming this one rather than org.manage_members is what makes a project-scope denial
  // actionable — the two are refused by different endpoints and asked for from different people.
  projectManageMembers: 'project.manage_members',
  // Creating an *account* is deliberately not implied by managing members: one assigns a role to
  // someone who exists, the other mints a principal and burns an email address for good.
  orgCreateUsers: 'org.create_users',
  // ...and switching an account off, or setting a password on it, are each their own grant again
  // (E23-T23) — creating, revoking and impersonating-by-password are three different powers.
  orgDeactivateUsers: 'org.deactivate_users',
  orgResetUserPasswords: 'org.reset_user_passwords',
  platformManageOrgs: 'platform.manage_orgs',
  // Reading Platform's trail (E23-T1) is deliberately not a corollary of managing members: a
  // compliance reviewer should be able to read history and change nothing.
  viewAudit: 'org.view_audit',
  // Destroying something in Trash for good (E23-T21) — deliberately not implied by any delete
  // permission, in either service, so a 403 here is an ordinary answer worth naming.
  trashPurge: 'trash.purge',
  // The multi-instance surface (E28): the instance registry and the pool an org/project/app runs on.
  instancesManage: 'instances.manage',
  devicesManage: 'devices.manage',
  alertsDelete: 'alerts.delete',
  executionsDelete: 'executions.delete',
  // Live activity (E51): counts, who-is-where, and the log are three separate grants, admins-only by default.
  activityViewMetrics: 'activity.view_metrics',
  activityViewPresence: 'activity.view_presence',
  activityViewLog: 'activity.view_log',
} as const

export type Permission = (typeof PERMISSIONS)[keyof typeof PERMISSIONS]

/**
 * What a 403 reads as. The cache note is not padding: `IPlatformAuthz` caches both outcomes for
 * `Platform:AuthzCacheSeconds` (60 by default), so someone whose role was granted seconds ago keeps
 * seeing this until the cached deny expires (E16-T2's live finding). Without the sentence, the
 * obvious reading of "you were just granted the role and it still says no" is that the grant failed.
 */
export function forbidden(action: string, permission?: Permission): string {
  const needs = permission ? ` It needs the “${permission}” permission.` : ''
  return `You don't have permission to ${action}.${needs} Ask an organization or project admin for it: ` +
    `a permission granted in the last minute can take that long to take effect.`
}

interface ErrorBody {
  error?: string
  /** Set by the services' unhandled-exception handler; the id to grep for in the log file. */
  traceId?: string
  detail?: string
  /** `limit_reached` on an over-limit refusal (E42, HTTP 409), with `message` saying which and how many. */
  code?: string
  message?: string
  key?: string
  limit?: number
  used?: number
}

function response(err: unknown): { status?: number; data?: ErrorBody } | undefined {
  return (err as AxiosError<ErrorBody>).response
}

/** The server's own message for this failure, when it sent one. */
export function serverError(err: unknown): string | null {
  const data = response(err)?.data
  // A limit refusal (E42, any plan since E46) carries its sentence in `message` ("Plan limit reached: Projects in
  // the organization — 2 of 2 used."), not `error` — surfaced so every panel shows it verbatim.
  if (data?.code === 'limit_reached') return data.message ?? 'Plan limit reached.'
  return data?.error ?? null
}

/** Whether a failure is an over-limit refusal (E42) rather than a bad request. */
export function isLimitReached(err: unknown): boolean {
  return response(err)?.data?.code === 'limit_reached'
}

export function isForbidden(err: unknown): boolean {
  return response(err)?.status === 403
}

/**
 * A failure that never reached a server: the request was made and no response came back.
 *
 * This is the case the old single fallback served worst. The browser reports a dead service and a
 * CORS refusal identically — same event, same empty response, `err.response` undefined in both — so
 * "is the API running?" was a coin flip that sent half of its readers to restart a service that was
 * running fine, with no way to discover their mistake.
 */
export function isUnreachable(err: unknown): boolean {
  const axiosError = err as AxiosError
  return !axiosError?.response && axiosError?.code !== 'ERR_CANCELED'
}

/** The trace id a 500 carried, so the reader can find the stack in the service's log file. */
export function traceId(err: unknown): string | null {
  return response(err)?.data?.traceId ?? null
}

/**
 * The message to show for a failed request.
 *
 * @param action what the user was trying to do, as a verb phrase ("save the pipeline") — it completes
 *   both the denial and the fallback, so it reads in either.
 * @param permission the code this call requires, when it is known; omitted for calls that span several.
 * @param fallback what to say for anything that isn't a 403 and carries no server message — typically
 *   the "is the service running?" line, which is only honest once a denial has been ruled out.
 *
 * A non-403 server message is passed through **verbatim**: DataFlow's 400s explain themselves (which
 * fixture failed the publish gate, why a resume isn't possible), and flattening them to "failed" strands
 * the reader on the one screen that could have told them what to do.
 *
 * The `fallback` is no longer the whole answer for a transport failure, a 404 or a 500 — each of those
 * now describes what actually happened. But the caller's half is *kept*, because "Failed to load
 * pipelines" is context this module doesn't have and a diagnosis with no subject ("something hit an
 * error") is worse than the message it replaced. What gets dropped is only the fallback's trailing
 * "…is the API running?" guess, which by that point has either been answered or contradicted.
 */
export function describeApiError(
  err: unknown,
  action: string,
  fallback: string,
  permission?: Permission,
): string {
  // E57: not a missing permission — the account is read-only until its email is verified (both APIs say so).
  if (response(err)?.data?.error === 'email_verification_required') {
    return 'Verify your email address to make changes: use the banner at the top of the page to get a code.'
  }
  if (isForbidden(err)) return forbidden(action, permission)

  const service = serviceForUrl((err as AxiosError)?.config?.baseURL)
  const name = service?.label ?? 'the API'
  const subject = whatFailed(fallback)

  if (isUnreachable(err)) return `${subject} ${unreachable(err, service)}`

  const status = response(err)?.status ?? 0

  // A 500 body carries no explanation on purpose (it could name a table or a connection string), so
  // the useful thing to hand over is the reference that finds the stack trace in the log file.
  if (status >= 500) {
    const reference = traceId(err)
    // Platform's own log is readable in the Console (Service logs), so a platform admin needs no shell.
    const where = service
      ? ` (${service.id === 'platform' ? `Console › Service logs, or ${service.logHint}` : service.logHint})`
      : ''
    return reference
      ? `${subject} ${serverError(err) ?? `${name} hit an error handling the request.`} Its log${where} has ` +
        `the details: search for ${reference}.`
      : `${subject} ${serverError(err) ?? `${name} hit an error handling the request.`} ` +
        `Check the service's log${where} for the exception.`
  }

  // A 404 from a service that answered is not "it's down" — it's an address problem, and the two
  // addresses that go wrong are the base URL (pointed at the wrong service) and the slug in the path.
  if (status === 404) {
    const path = (err as AxiosError)?.config?.url ?? ''
    const explanation = serverError(err) ??
      `${name} answered, but has nothing at ${path}. Either the slug in that path doesn't exist, ` +
      `or the base URL points at a different service than the one expected.`
    return `${subject} ${explanation}`
  }

  const feature = disabledFeature(err)
  if (feature) return featureDisabled(action, feature)

  return serverError(err) ?? fallback
}

/**
 * The feature key a refusal named, or null when the failure was something else (E26-T9).
 *
 * The services refuse an ungranted capability with a 400 whose message ends in `(feature 'x.y')` — one
 * sentence, produced from one place in each service, which is why matching it here is a contract rather
 * than a guess at prose. It is deliberately anchored to that suffix: a message that merely *mentions* a
 * key (a validation error about a config field, say) is not a refusal and must not be re-dressed as one.
 */
export function disabledFeature(err: unknown): string | null {
  const message = serverError(err)
  if (!message || isForbidden(err)) return null
  return /\(feature '([a-z0-9.\-_]+)'\)\.?\s*$/i.exec(message)?.[1] ?? null
}

/**
 * What an ungranted capability reads as.
 *
 * The distinction this draws is the whole point: a 400 saying "not enabled for this project" looked, in
 * a panel that shows it beside connection errors, exactly like something broken — so a Campaigns tab
 * that had simply never been switched on read as a bug, and the reader's next move was to check whether
 * a service was down. **Nothing is wrong here.** The screen works, the project was not granted the
 * capability, and the fix is a switch on the Features tab — which is a different person to ask than a
 * permission (a platform admin, not a project admin), so saying which one matters.
 */
export function featureDisabled(action: string, featureKey: string): string {
  return `This project isn't set up to ${action}. It needs the “${featureKey}” capability, which is ` +
    `switched off: a platform admin can turn it on for the organization or the project under Features. ` +
    `Nothing is broken.`
}

/**
 * The caller's fallback with its trailing guess removed.
 *
 * Every fallback in this app follows one shape, "Failed to load pipelines: is the DataFlow API
 * running?", where the first half says what the user lost and the second half guesses why. Once the
 * why is actually known, keeping the guess would produce messages that argue with themselves
 * ("…is the API running? The API hit an error handling the request"), so only the subject survives.
 * A fallback written without the clause passes through unchanged; an older one with a dash still splits.
 */
function whatFailed(fallback: string): string {
  const subject = fallback.split(/\s+[—–-]\s+|:\s+/)[0].trim()
  return /[.!?]$/.test(subject) ? subject : `${subject}.`
}

/**
 * E66: an error message in two layers — a sentence anyone can act on, then the technical detail for
 * whoever fixes it. Carried in the one string every store already passes around (an invisible separator
 * between the two, so nothing that stores or compares messages changes); {@link ErrorBanner} shows the
 * sentence with a "Details" toggle, and anything else that prints the string still prints all of it.
 */
const DETAILS_MARK = '⁣'
/** Set on a message about a service not answering: the banner offers "Check status" beside it. */
const STATUS_MARK = '⁤'

export function withDetails(message: string, details: string, offerStatus = false): string {
  return `${message}${offerStatus ? STATUS_MARK : ''}${DETAILS_MARK}${details}`
}

export function splitDetails(message: string): { message: string; details: string | null; offerStatus: boolean } {
  const offerStatus = message.includes(STATUS_MARK)
  const [main, ...rest] = message.replace(STATUS_MARK, '').split(DETAILS_MARK)
  return { message: main.trim(), details: rest.length ? rest.join(' ').trim() : null, offerStatus }
}

/**
 * The message for a request that never got a response.
 *
 * What the reader sees is plain: the service is not answering, try again, check its status. The detail
 * underneath deliberately refuses to pick between "down" and "CORS", because from inside the browser the
 * two are not distinguishable — any confident claim would be wrong half the time. What it can do is give
 * whoever fixes it the one test that *does* separate them: open `/readyz` directly. A service that
 * answers there but not here is refusing the origin; one that answers neither is down.
 */
function unreachable(err: unknown, service: ServiceDescriptor | null): string {
  const name = service?.label ?? 'The service'
  // The top bar's "Service problem" signal reacts now, rather than at its next health check.
  if (service) useServiceStatusStore.getState().reportUnreachable(service.label)
  const at = service ? ` at ${service.baseUrl}` : ''

  if ((err as AxiosError)?.code === 'ECONNABORTED') {
    return withDetails(
      `${name} is taking too long to answer. It may be busy or starting up: try again in a moment.`,
      `${name}${at} did not answer in time. It may be starting up, or blocked on a slow ` +
        `database query: check ${service?.logHint ?? 'the service log'} for the request that hung.`,
      true,
    )
  }

  return withDetails(
    `${name} isn't responding right now. It may be restarting: try again in a moment, or check the service status.`,
    `Couldn't reach ${name}${at}. Two different things look identical to the browser: the ` +
      `service isn't running, or it is running and refused this page's origin at CORS. ` +
      `Open ${service ? service.baseUrl : 'the service'}/readyz in a new tab to tell them apart: ` +
      `if it answers, this is CORS and the origin ${window.location.origin} needs adding under the ` +
      `project's Origins tab; if it doesn't, the service is down and its startup log says why` +
      (service ? ` (${service.logHint})` : '') + '.',
    true,
  )
}
