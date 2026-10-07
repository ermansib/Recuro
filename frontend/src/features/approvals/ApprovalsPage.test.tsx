import { screen } from '@testing-library/react'
import { renderWithProviders } from '../../test/render'
import { ApprovalsPage } from './ApprovalsPage'

describe('ApprovalsPage', () => {
  it('shows only the signed-in role’s queue', async () => {
    renderWithProviders(<ApprovalsPage />, { role: 'mdceo' })
    expect(await screen.findByText(/REQ-2026-0149/)).toBeInTheDocument()
  })
})
