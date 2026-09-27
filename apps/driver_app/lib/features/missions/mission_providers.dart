import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import 'mission_repository.dart';

final missionRepositoryProvider = Provider<MissionRepository>(
  (ref) => MissionRepository(
    ref.watch(apiClientProvider),
    ref.watch(fileDActionsProvider),
  ),
);
