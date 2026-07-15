import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter } from './Card';

describe('<Card />', () => {
  it('renders a surface with the card token and lg radius', () => {
    render(<Card data-testid="card">body</Card>);
    const card = screen.getByTestId('card');
    expect(card.className).toContain('bg-card');
    expect(card.className).toContain('rounded-lg');
  });

  it('composes header, title, description, content and footer', () => {
    render(
      <Card>
        <CardHeader>
          <CardTitle>Sunset Villa</CardTitle>
          <CardDescription>2 guests · sea view</CardDescription>
        </CardHeader>
        <CardContent>From $220 / night</CardContent>
        <CardFooter>Book</CardFooter>
      </Card>,
    );
    expect(screen.getByText('Sunset Villa')).toBeInTheDocument();
    expect(screen.getByText('2 guests · sea view')).toBeInTheDocument();
    expect(screen.getByText('From $220 / night')).toBeInTheDocument();
    expect(screen.getByText('Book')).toBeInTheDocument();
  });

  it('renders CardTitle as a level-3 heading by default', () => {
    render(<CardTitle>Sunset Villa</CardTitle>);
    expect(screen.getByRole('heading', { level: 3, name: 'Sunset Villa' })).toBeInTheDocument();
  });

  it('renders CardDescription in muted foreground', () => {
    render(<CardDescription>sea view</CardDescription>);
    expect(screen.getByText('sea view').className).toContain('text-muted-foreground');
  });

  it('merges a caller className on the root', () => {
    render(
      <Card data-testid="card" className="max-w-sm">
        x
      </Card>,
    );
    const card = screen.getByTestId('card');
    expect(card.className).toContain('max-w-sm');
    expect(card.className).toContain('bg-card');
  });
});
