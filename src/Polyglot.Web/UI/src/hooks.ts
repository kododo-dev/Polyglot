import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError, ForbiddenError, SessionExpiredError } from '@kododo-dev/way-shell'

/** What a failed request means to the user; nothing for an expired session, which the shell's dialog explains. */
export function errorMessages(error: unknown, t: (key: string) => string): string[] {
  if (error instanceof SessionExpiredError) return []
  if (error instanceof ApiError) return error.messages
  if (error instanceof ForbiddenError) return [t('forbidden')]
  return [error instanceof Error ? error.message : String(error)]
}

/** Loads data on mount and whenever `load` changes; `reload` loads it again, keeping the old data meanwhile. */
export function useLoad<T>(load: () => Promise<T>) {
  const [data, setData] = useState<T>()
  const [error, setError] = useState<unknown>()
  const latest = useRef(0)

  const reload = useCallback(async () => {
    const call = ++latest.current
    try {
      const result = await load()
      if (call === latest.current) {
        setData(result)
        setError(undefined)
      }
    } catch (e) {
      if (call === latest.current) setError(e)
    }
  }, [load])

  useEffect(() => {
    // Loading is what this effect is for; the state is set once the request is done.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void reload()
  }, [reload])

  return { data, error, reload }
}
