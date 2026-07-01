import { render, screen, waitFor } from '@testing-library/react';
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
import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from '@/src/components/ui/sheet';
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

  it('Sheet opens from a trigger and renders its title, description and close', async () => {
    const user = userEvent.setup();
    render(
      <Sheet>
        <SheetTrigger data-testid="primitives-sheet">Open nav</SheetTrigger>
        <SheetContent>
          <SheetHeader>
            <SheetTitle>Sheet title</SheetTitle>
            <SheetDescription>Sheet description</SheetDescription>
          </SheetHeader>
          <SheetClose data-testid="primitives-sheet-close">Dismiss</SheetClose>
        </SheetContent>
      </Sheet>,
    );

    await user.click(screen.getByTestId('primitives-sheet'));
    expect(await screen.findByText('Sheet title')).toBeInTheDocument();
    expect(screen.getByText('Sheet description')).toBeInTheDocument();

    // The custom SheetClose and the built-in corner close both dismiss the sheet.
    await user.click(screen.getByTestId('primitives-sheet-close'));
    await waitFor(() => {
      expect(screen.queryByText('Sheet title')).not.toBeInTheDocument();
    });
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
