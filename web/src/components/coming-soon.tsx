import { Card, CardContent } from '@/src/components/ui/card';

export function ComingSoon({ feature }: { feature: string }) {
  return (
    <Card>
      <CardContent className="flex h-64 items-center justify-center p-6 text-center">
        <div>
          <p className="text-lg font-medium">{feature}</p>
          <p className="mt-1 text-sm text-muted-foreground">
            Coming soon — this slice focuses on the app shell, dashboard, and accounts.
          </p>
        </div>
      </CardContent>
    </Card>
  );
}
