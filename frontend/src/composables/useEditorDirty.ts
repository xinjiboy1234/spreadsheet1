import { ref } from 'vue'

/**
 * Dirty-state helpers for EditorView (spec §5.2).
 * While previewDirty, template edits must not flip templateDirty.
 */
export function useEditorDirty() {
  const templateDirty = ref(false)
  const previewDirty = ref(false)

  function markPreview() {
    previewDirty.value = true
    templateDirty.value = false
  }

  function clearPreview() {
    previewDirty.value = false
  }

  function markTemplateEdit() {
    if (!previewDirty.value) {
      templateDirty.value = true
    }
  }

  function clearAll() {
    templateDirty.value = false
    previewDirty.value = false
  }

  function resetAfterSave() {
    clearAll()
  }

  return {
    templateDirty,
    previewDirty,
    markPreview,
    clearPreview,
    markTemplateEdit,
    clearAll,
    resetAfterSave,
  }
}
