export async function fillTestPassword(field, value) {
  try {
    await field.fill(value);
  } catch {
    // Playwright's original call log can include the fill argument.
    throw new Error('Could not fill the test password field. Check that it is visible and editable.');
  }
}
