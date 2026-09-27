import type { Metadata } from 'next';
import './globals.css';

export const metadata: Metadata = {
  title: 'Console HBA Delivery',
  description: 'Back-office HBA Delivery : courses, livreurs, clients, paramètres.',
};

export default function RacineLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="fr">
      <body>{children}</body>
    </html>
  );
}
