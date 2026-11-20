import { Link, Outlet } from 'react-router'

export default function App() {
  return (
    <div className="mx-auto max-w-4xl space-y-6 p-6">
      <header className="border-b pb-3">
        <Link to="/" className="text-lg font-semibold">
          AppTemplate
        </Link>
      </header>
      <main>
        <Outlet />
      </main>
    </div>
  )
}
