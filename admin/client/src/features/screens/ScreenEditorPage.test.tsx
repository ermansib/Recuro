import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import type { UpdateScreenRequest } from '../../api/types'
import { fakeApi, MRF_SCREEN, renderWithApi } from '../../test/render'
import { ScreenEditorPage } from './ScreenEditorPage'

function renderEditor(updateScreen = vi.fn((_key: string, _request: UpdateScreenRequest) => Promise.resolve(MRF_SCREEN))) {
  renderWithApi(<ScreenEditorPage />, {
    api: fakeApi({ getScreen: () => Promise.resolve(MRF_SCREEN), updateScreen }),
    route: '/tenant/screens/mrf',
    path: '/tenant/screens/:key',
  })
  return updateScreen
}

describe('ScreenEditorPage', () => {
  it('keeps locked fields shown and required, and core screens on', async () => {
    renderEditor()

    expect(await screen.findByRole('checkbox', { name: 'Show Grade' })).toBeDisabled()
    expect(screen.getByRole('checkbox', { name: 'Grade is required' })).toBeDisabled()
    expect(screen.getByRole('checkbox', { name: 'Screen is turned on' })).toBeDisabled()
  })

  it('makes a field optional when it is hidden', async () => {
    renderEditor()

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Show Location' }))

    const required = screen.getByRole('checkbox', { name: 'Location is required' })
    expect(required).not.toBeChecked()
    expect(required).toBeDisabled()
  })

  it('saves renamed labels, header text and the new order', async () => {
    const updateScreen = renderEditor()

    await userEvent.type(await screen.findByLabelText('Title'), 'Raise a hiring request')
    await userEvent.type(screen.getByRole('textbox', { name: 'Label for Location' }), 'Branch')
    await userEvent.click(screen.getByRole('button', { name: 'Move Salary band up' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(updateScreen).toHaveBeenCalled())
    const [key, request] = updateScreen.mock.calls[0]!
    expect(key).toBe('mrf')
    expect(request.title).toBe('Raise a hiring request')
    expect(request.subtitle).toBeNull()
    expect(request.fields.map((f) => [f.key, f.label, f.sortOrder])).toEqual([
      ['grade', null, 10],
      ['band', null, 20],
      ['location', 'Branch', 30],
    ])
  })
})
