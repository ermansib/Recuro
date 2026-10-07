import type { EffectiveField, EffectiveScreen, UpdateScreenRequest } from '../../api/types'

/** Editable copy of a screen. Empty strings mean "use the product default". */
export interface FieldDraft {
  key: string
  label: string
  defaultLabel: string
  dataType: EffectiveField['dataType']
  isVisible: boolean
  isRequired: boolean
  isLocked: boolean
}

export interface ScreenDraft {
  isEnabled: boolean
  title: string
  subtitle: string
  fields: FieldDraft[]
}

const SORT_STEP = 10

function toFieldDraft(field: EffectiveField, useDefaults: boolean): FieldDraft {
  return {
    key: field.key,
    label: useDefaults || field.label === field.defaultLabel ? '' : field.label,
    defaultLabel: field.defaultLabel,
    dataType: field.dataType,
    isVisible: useDefaults ? true : field.isVisible,
    isRequired: useDefaults ? field.defaultRequired : field.isRequired,
    isLocked: field.isLocked,
  }
}

export function draftFrom(screen: EffectiveScreen): ScreenDraft {
  return {
    isEnabled: screen.isEnabled,
    title: screen.title === screen.defaultTitle ? '' : screen.title,
    subtitle: screen.subtitle === screen.defaultSubtitle ? '' : screen.subtitle,
    fields: screen.fields.map((field) => toFieldDraft(field, false)),
  }
}

export function defaultDraft(screen: EffectiveScreen): ScreenDraft {
  return {
    isEnabled: true,
    title: '',
    subtitle: '',
    fields: [...screen.fields]
      .sort((a, b) => a.defaultSortOrder - b.defaultSortOrder)
      .map((field) => toFieldDraft(field, true)),
  }
}

export function moveField(fields: FieldDraft[], index: number, step: -1 | 1): FieldDraft[] {
  const target = index + step
  if (target < 0 || target >= fields.length) return fields
  const next = [...fields]
  const [moved] = next.splice(index, 1)
  if (moved) next.splice(target, 0, moved)
  return next
}

/** Hiding a field also makes it optional, because a hidden field cannot be required. */
export function setVisible(field: FieldDraft, isVisible: boolean): FieldDraft {
  return { ...field, isVisible, isRequired: isVisible ? field.isRequired : false }
}

export function toRequest(draft: ScreenDraft): UpdateScreenRequest {
  return {
    isEnabled: draft.isEnabled,
    title: draft.title.trim() || null,
    subtitle: draft.subtitle.trim() || null,
    fields: draft.fields.map((field, index) => ({
      key: field.key,
      label: field.label.trim() || null,
      isVisible: field.isVisible,
      isRequired: field.isRequired,
      sortOrder: (index + 1) * SORT_STEP,
    })),
  }
}
