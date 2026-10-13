import { useTitle } from '@/hooks'

export function NotFoundPage() {
  useTitle('Not found · AutoRemediator')
  return (
    <>
      <h1 className="text-2xl font-semibold">Not Found</h1>
      <p className="text-muted-foreground">Sorry, the content you are looking for does not exist.</p>
    </>
  )
}
