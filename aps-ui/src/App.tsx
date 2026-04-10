import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom"

import { Layout } from "./components/Layout"
import { ApsProvider } from "./context/ApsContext"
import { BomPage } from "./pages/BomPage"
import { CampaignsPage } from "./pages/CampaignsPage"
import { CtpPage } from "./pages/CtpPage"
import { DashboardPage } from "./pages/DashboardPage"
import { MasterDataPage } from "./pages/MasterDataPage"
import {
  CapacityPage,
  DispatchPage,
  MaterialPage,
  OrdersPage,
  SchedulePage,
  ScenariosPage,
} from "./pages/PlanningScreens"

function App() {
  return (
    <BrowserRouter>
      <ApsProvider>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<DashboardPage />} />
            <Route path="campaigns" element={<CampaignsPage />} />
            <Route path="schedule" element={<SchedulePage />} />
            <Route path="orders" element={<OrdersPage />} />
            <Route path="dispatch" element={<DispatchPage />} />
            <Route path="material" element={<MaterialPage />} />
            <Route path="capacity" element={<CapacityPage />} />
            <Route path="scenarios" element={<ScenariosPage />} />
            <Route path="ctp" element={<CtpPage />} />
            <Route path="bom" element={<BomPage />} />
            <Route path="master-data" element={<MasterDataPage />} />
            <Route path="gantt" element={<Navigate to="/schedule" replace />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </ApsProvider>
    </BrowserRouter>
  )
}

export default App
