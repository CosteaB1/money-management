import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { Card, CardContent, CardFooter } from '@/src/components/ui/card';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/src/components/ui/dropdown-menu';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
} from '@/src/components/ui/select';
import { Table, TableBody, TableCaption, TableCell, TableRow } from '@/src/components/ui/table';

describe('UI primitives — rarely-used subcomponents', () => {
  it('Card renders a footer', () => {
    render(
      <Card>
        <CardContent>body</CardContent>
        <CardFooter>footer content</CardFooter>
      </Card>,
    );
    expect(screen.getByText('footer content')).toBeInTheDocument();
  });

  it('Table renders a caption', () => {
    render(
      <Table>
        <TableCaption>my caption</TableCaption>
        <TableBody>
          <TableRow>
            <TableCell>cell</TableCell>
          </TableRow>
        </TableBody>
      </Table>,
    );
    expect(screen.getByText('my caption')).toBeInTheDocument();
  });

  it('Select renders a label and separator inside the content', async () => {
    const user = userEvent.setup();
    render(
      <Select defaultValue="a">
        <SelectTrigger data-testid="primitives-select">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectLabel>Group label</SelectLabel>
            <SelectItem value="a">Option A</SelectItem>
            <SelectSeparator />
            <SelectItem value="b">Option B</SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>,
    );
    await user.click(screen.getByTestId('primitives-select'));
    expect(await screen.findByText('Group label')).toBeInTheDocument();
  });

  it('DropdownMenu renders a label and separator', async () => {
    const user = userEvent.setup();
    render(
      <DropdownMenu>
        <DropdownMenuTrigger data-testid="primitives-menu">Open</DropdownMenuTrigger>
        <DropdownMenuContent>
          <DropdownMenuLabel>Menu label</DropdownMenuLabel>
          <DropdownMenuSeparator />
          <DropdownMenuItem>Item</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>,
    );
    await user.click(screen.getByTestId('primitives-menu'));
    expect(await screen.findByText('Menu label')).toBeInTheDocument();
  });
});
