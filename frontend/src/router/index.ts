import { createRouter, createWebHistory } from 'vue-router'
import EditorView from '../views/EditorView.vue'
import ListView from '../views/ListView.vue'
import VersionsView from '../views/VersionsView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      name: 'list',
      component: ListView,
    },
    {
      path: '/editor/:id',
      name: 'editor',
      component: EditorView,
    },
    {
      path: '/editor/:id/versions',
      name: 'versions',
      component: VersionsView,
    },
  ],
})

export default router
