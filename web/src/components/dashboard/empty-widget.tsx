import { Card, CardContent, CardHeader, CardTitle } from '@/src/components/ui/card';

interface Props {
  title: string;
  hint: string;
}

export function EmptyWidget({ title, hint }: Props) {
  return (
    <Card className="h-full">
      <CardHeader>
        <CardTitle className="text-sm font-medium text-muted-foreground">{title}</CardTitle>
      </CardHeader>
      <CardContent className="flex h-32 items-center justify-center text-center">
        <p className="text-sm text-muted-foreground">{hint}</p>
      </CardContent>
    </Card>
  );
}
