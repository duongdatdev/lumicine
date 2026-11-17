import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import React from 'react';
import App from './App';

describe('App Bootstrap and Navigation', () => {
  it('renders application brand, navigation links, and footer', () => {
    render(<App />);
    expect(screen.getAllByText(/LumiCine/i)[0]).toBeInTheDocument();
    expect(screen.getByText('Phim')).toBeInTheDocument();
    expect(screen.getByText('Rạp chiếu')).toBeInTheDocument();
    expect(screen.getAllByText('Tìm phim AI')[0]).toBeInTheDocument();
    expect(screen.getAllByText(/Trải nghiệm điện ảnh đỉnh cao/i)[0]).toBeInTheDocument();
  });
});
