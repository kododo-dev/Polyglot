// What the pages' APIs return; mirrors the records in Ways/PolyglotWays.cs.

export type UserRole = 'None' | 'Editor' | 'Admin'

export interface DemoKey {
  token: string
  baseUrl: string
  culture: string
  apiReferenceUrl: string | null
}

export interface Overview {
  cultures: string[]
  defaultCulture: string
  userCount: number
  disabledUserCount: number
  keyCount: number
  activeKeyCount: number
  apiEnabled: boolean
  apiReferenceUrl: string | null
  openApiUrl: string | null
  demoKey: DemoKey | null
}

export interface HistoryQuery {
  key?: string
  culture?: string
  userId?: string
  from?: string
  to?: string
  before?: number
}

export interface Change {
  id: number
  key: string
  culture: string
  oldValue: string | null
  newValue: string | null
  userName: string
  changedAt: string
}

export interface History {
  changes: Change[]
  olderThan: number | null
  cultures: string[]
  users: { id: string; name: string }[]
}

export interface User {
  id: string
  username: string
  displayName: string
  role: UserRole
  isDisabled: boolean
  hasPassword: boolean
  isExternal: boolean
}

export interface Users {
  users: User[]
  currentUserId: string | null
  readOnly: boolean
  minPasswordLength: number
}

export interface ApiKey {
  id: string
  name: string
  prefix: string
  status: 'active' | 'expired' | 'disabled'
  isDemo: boolean
  createdAt: string
  lastUsedAt: string | null
  expiresAt: string | null
}

export interface ApiKeys {
  keys: ApiKey[]
  apiEnabled: boolean
  baseUrl: string
  defaultCulture: string
  maxNameLength: number
  apiReferenceUrl: string | null
  demoKey: DemoKey | null
}

export interface CreatedKey {
  name: string
  token: string
}

export interface Account {
  username: string
  displayName: string
  role: UserRole
  hasPassword: boolean
  isDemo: boolean
  minPasswordLength: number
}
