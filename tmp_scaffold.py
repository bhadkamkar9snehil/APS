import os

cwd = r'c:\Users\bhadk\Documents\APS\aps-ui\src'

def write_file(filename, content):
    with open(os.path.join(cwd, filename), 'w', encoding='utf-8') as f:
        f.write(content.strip())

os.makedirs(os.path.join(cwd, 'components'), exist_ok=True)
os.makedirs(os.path.join(cwd, 'pages'), exist_ok=True)

# Layout.tsx
write_file('components/Layout.tsx', """
import { Outlet, NavLink } from "react-router-dom";
import { LayoutDashboard, Calendar, Trello, ShoppingCart, Map, PackageSearch, Activity, FileText, Settings, Database } from "lucide-react";

const NAV_GROUPS = [
  {
    title: "PLANNING",
    items: [
      { name: "Dashboard", path: "/", icon: LayoutDashboard },
      { name: "Campaigns", path: "/campaigns", icon: Trello },
      { name: "Gantt Schedule", path: "/gantt", icon: Calendar },
      { name: "Sales Orders", path: "/orders", icon: ShoppingCart },
    ]
  },
  {
    title: "ANALYSIS",
    items: [
      { name: "Capacity Map", path: "/capacity", icon: Map },
      { name: "Material Plan", path: "/material", icon: PackageSearch },
      { name: "Equipment Dispatch", path: "/dispatch", icon: Activity },
      { name: "CTP Check", path: "/ctp", icon: FileText },
      { name: "Scenarios", path: "/scenarios", icon: Settings },
    ]
  },
  {
    title: "CONFIGURATION",
    items: [
      { name: "Master Data", path: "/master-data", icon: Database },
    ]
  }
];

export function Layout() {
  return (
    <div className="flex h-screen w-full bg-slate-50 text-slate-900 font-sans">
      {/* Sidebar */}
      <aside className="w-64 bg-slate-900 text-slate-300 flex flex-col shadow-xl z-10 shrink-0">
        <div className="h-16 flex items-center px-6 border-b border-slate-800">
          <div className="flex items-center gap-2 font-bold text-xl tracking-wide text-white">
            <div className="w-8 h-8 rounded bg-blue-600 flex items-center justify-center text-sm">APS</div>
            SteelAPS
          </div>
        </div>
        
        <div className="flex-1 overflow-y-auto py-6 px-4 flex flex-col gap-8">
          {NAV_GROUPS.map(group => (
            <div key={group.title}>
              <div className="text-xs font-semibold text-slate-500 mb-3 tracking-wider px-2">
                {group.title}
              </div>
              <div className="flex flex-col gap-1">
                {group.items.map(item => (
                  <NavLink
                    key={item.name}
                    to={item.path}
                    className={({isActive}) => 
                      `flex flex-row items-center gap-3 px-3 py-2 rounded-md transition-colors ${
                        isActive 
                          ? "bg-blue-600/10 text-blue-400 font-medium" 
                          : "hover:bg-slate-800 hover:text-slate-100"
                      }`
                    }
                  >
                    <item.icon className="w-4 h-4" />
                    <span className="text-sm">{item.name}</span>
                  </NavLink>
                ))}
              </div>
            </div>
          ))}
        </div>
      </aside>

      {/* Main Content Area */}
      <main className="flex-1 overflow-hidden flex flex-col relative w-full h-full">
        <Outlet />
      </main>
    </div>
  );
}
""")

# App.tsx
write_file('App.tsx', """
import { BrowserRouter, Routes, Route } from "react-router-dom";
import { Layout } from "./components/Layout";

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<div className="p-8"><h1>Dashboard (Coming Soon)</h1></div>} />
          <Route path="/master-data" element={<div className="p-8"><h1>Master Data Settings</h1></div>} />
          {/* Catch all for unbuilt pages */}
          <Route path="*" element={<div className="p-8 text-muted-foreground">Screen under construction.</div>} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}

export default App;
""")
