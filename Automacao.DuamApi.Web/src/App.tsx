import { Navigate, Outlet, Route, Routes } from "react-router-dom";
import { Sidebar } from "./components/Sidebar";
import { Topbar } from "./components/Topbar";
import { LoginPage } from "./pages/LoginPage";
import { UploadPage } from "./pages/UploadPage";
import { JobListPage } from "./pages/JobListPage";
import { JobDetailPage } from "./pages/JobDetailPage";
import { getSession } from "./lib/authStorage";

function AuthenticatedLayout() {
  if (!getSession()) {
    return <Navigate to="/login" replace />;
  }

  return (
    <div className="app-shell">
      <div className="app-window">
        <Sidebar />
        <div className="content-area">
          <Topbar />
          <Outlet />
        </div>
      </div>
    </div>
  );
}

function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<AuthenticatedLayout />}>
        <Route path="/" element={<UploadPage />} />
        <Route path="/acompanhamento" element={<JobListPage />} />
        <Route path="/acompanhamento/:jobId" element={<JobDetailPage />} />
      </Route>
    </Routes>
  );
}

export default App;
