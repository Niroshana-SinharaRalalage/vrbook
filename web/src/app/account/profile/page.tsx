import { ProfileForm } from './ProfileForm';

// VRB-108 — guest profile (view + edit name/phone, read-only email + loyalty
// tier). The interactive form is a client component; this route is a thin shell.
const AccountProfilePage = () => <ProfileForm />;

export default AccountProfilePage;
